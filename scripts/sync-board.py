#!/usr/bin/env python3
"""Sync the 'Whetstone — build order' project board from GitHub issue state.

First reconciles each issue's **Blocked by:** line into GitHub's native issue
dependencies (additive — see wire_declared), then reads the resulting graph and
writes four project fields:

  Status         Blocked / Ready / In progress / In review / Done
  Blockers       count of OPEN issues directly blocking this one
  Unlocks        count of issues transitively unblocked by closing this one
  Critical path  on the longest remaining dependency chain, or not

Status is only ever moved between Blocked, Ready and Done. 'In progress' and
'In review' are set by hand and are never overwritten.

Idempotent. Run it after closing issues, or let .github/workflows/board-sync.yml
run it for you.

    python3 scripts/sync-board.py [--dry-run] [--no-wire]
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
import time
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor

OWNER = "armaatus"
REPO = "whetstone"
PROJECT_NUMBER = 1
DRY = "--dry-run" in sys.argv
WIRE = "--no-wire" not in sys.argv


TRANSIENT = ("temporary conflict", "was submitted too quickly", "secondary rate limit",
             "abuse detection", "502 Bad Gateway", "was rate limited")


def gh(args: list[str], attempts: int = 6) -> str:
    """Run gh, retrying the conflict/rate-limit errors the Projects API likes to throw."""
    for attempt in range(attempts):
        r = subprocess.run(["gh", *args], capture_output=True, text=True)
        if r.returncode == 0:
            return r.stdout
        if attempt < attempts - 1 and any(t in r.stderr for t in TRANSIENT):
            time.sleep(1.5 * (attempt + 1))
            continue
        raise RuntimeError(f"gh {' '.join(args)}\n{r.stderr}")
    raise AssertionError("unreachable")


def graphql(query: str, **variables) -> dict:
    args = ["api", "graphql", "-f", f"query={query}"]
    for k, v in variables.items():
        args += ["-F", f"{k}={v}"]
    return json.loads(gh(args))


# ---------------------------------------------------------------- issues

def fetch_issues() -> dict[int, dict]:
    raw = json.loads(gh([
        "api", f"repos/{OWNER}/{REPO}/issues", "--paginate", "-X", "GET",
        "-f", "state=all", "-f", "per_page=100",
    ]))
    return {
        i["number"]: {
            "id": i["id"],                 # database id — what the dependencies API wants
            "node_id": i["node_id"],       # GraphQL id — what the Projects API wants
            "state": i["state"],
            "title": i["title"],
            "body": i.get("body") or "",
        }
        for i in raw if "pull_request" not in i
    }


# "**Depends on:**" is a synonym some tickets use. Parse both, or a ticket that spells it
# the other way is silently read as having no blockers at all.
BLOCKED_BY = re.compile(r"^\s*\*\*(?:Blocked by|Depends on):\*\*(.*)$", re.MULTILINE)
NO_BLOCKERS = re.compile(r"^\s*(none|nothing|n/?a)\b", re.IGNORECASE)


def declared_blockers(body: str) -> tuple[list[int], str | None]:
    """Read an issue's '**Blocked by:** #12, #34' line.

    Returns the issue numbers, plus the raw line when it says something this can't
    parse into numbers ('P1, P2, P3'), so the caller can surface it rather than
    silently treating the issue as unblocked.
    """
    m = BLOCKED_BY.search(body)
    if not m:
        return [], None
    line = m.group(1).strip()
    # "nothing. ADR-001 must be written after reading #99" declares no blockers;
    # the #99 is prose. An explicit no wins over any issue reference on the line.
    if NO_BLOCKERS.match(line):
        return [], None
    numbers = [int(x) for x in re.findall(r"#(\d+)", line)]
    return (numbers, None) if numbers else ([], line)


def wire_declared(issues: dict[int, dict], existing: dict[int, list[int]]) -> dict[int, list[int]]:
    """Turn '**Blocked by:** #n' lines into native GitHub issue dependencies.

    Additive only. A dependency present in the graph but absent from the body is
    left alone: removing an edge changes the build order, and this script should
    not do that silently on the strength of a prose edit. Unwire by hand.
    """
    graph = {n: list(bs) for n, bs in existing.items()}
    added, unparsed = [], []
    for n, issue in sorted(issues.items()):
        want, odd = declared_blockers(issue["body"])
        if odd:
            unparsed.append((n, odd))
        for b in want:
            if b == n or b not in issues or b in graph.get(n, ()):
                continue
            added.append((n, b))
            if not DRY:
                gh(["api", "--method", "POST",
                    f"repos/{OWNER}/{REPO}/issues/{n}/dependencies/blocked_by",
                    "-F", f"issue_id={issues[b]['id']}", "--silent"])
            graph.setdefault(n, []).append(b)

    for n, line in unparsed:
        print(f"  note: #{n} has a 'Blocked by' line this cannot parse: {line[:70]!r}")

    # This function only ever adds. An edge that is in the graph but not in the body can
    # therefore never be removed by a run — it just quietly keeps blocking. Say so, loudly,
    # so a stale or mis-parsed edge is a line of output rather than a permanent wrong answer.
    undeclared = []
    for n, bs in sorted(graph.items()):
        if n not in issues:
            continue
        want, _ = declared_blockers(issues[n]["body"])
        for b in bs:
            if b not in want:
                undeclared.append((n, b))
    if undeclared:
        print(f"{len(undeclared)} wired dependencies are NOT declared in the issue body "
              f"(this script cannot remove them — unwire by hand):")
        for n, b in undeclared[:20]:
            print(f"  #{n} <- #{b}")
        if len(undeclared) > 20:
            print(f"  ... and {len(undeclared) - 20} more")
    if added:
        verb = "would add" if DRY else "added"
        print(f"{verb} {len(added)} dependencies: " +
              ", ".join(f"#{c}<-#{b}" for c, b in added[:12]) +
              (" ..." if len(added) > 12 else ""))
    return graph


def fetch_blockers(numbers: list[int]) -> dict[int, list[int]]:
    """number -> list of issue numbers directly blocking it (open and closed)."""
    def one(n: int) -> tuple[int, list[int]]:
        try:
            rows = json.loads(gh([
                "api", f"repos/{OWNER}/{REPO}/issues/{n}/dependencies/blocked_by",
                "--paginate",
            ]))
        except RuntimeError:
            return n, []
        return n, [r["number"] for r in rows]

    with ThreadPoolExecutor(8) as ex:
        return dict(ex.map(one, numbers))


# ---------------------------------------------------------------- graph

def transitive_unlocks(blockers: dict[int, list[int]], open_set: set[int]) -> dict[int, int]:
    """How many still-open issues each issue transitively gates."""
    blocks = defaultdict(set)
    for child, parents in blockers.items():
        for p in parents:
            blocks[p].add(child)

    memo: dict[int, set[int]] = {}

    def reach(n: int, seen: frozenset[int] = frozenset()) -> set[int]:
        if n in memo:
            return memo[n]
        if n in seen:                       # defensive; the graph is a DAG
            return set()
        out: set[int] = set()
        for c in blocks.get(n, ()):
            out.add(c)
            out |= reach(c, seen | {n})
        memo[n] = out
        return out

    return {n: len(reach(n) & open_set) for n in blockers.keys() | blocks.keys()}


def critical_path(blockers: dict[int, list[int]], open_set: set[int]) -> set[int]:
    """Nodes on a longest chain through the still-open subgraph."""
    parents = {n: [p for p in ps if p in open_set] for n, ps in blockers.items() if n in open_set}
    children = defaultdict(list)
    for n, ps in parents.items():
        for p in ps:
            children[p].append(n)
    nodes = open_set

    up, down = {}, {}

    def depth_up(n: int) -> int:
        if n in up:
            return up[n]
        up[n] = 0
        up[n] = 1 + max((depth_up(p) for p in parents.get(n, ())), default=-1)
        return up[n]

    def depth_down(n: int) -> int:
        if n in down:
            return down[n]
        down[n] = 0
        down[n] = 1 + max((depth_down(c) for c in children.get(n, ())), default=-1)
        return down[n]

    lengths = {n: depth_up(n) + depth_down(n) + 1 for n in nodes}
    longest = max(lengths.values(), default=0)
    return {n for n, l in lengths.items() if l == longest}


# ---------------------------------------------------------------- project

def project_context() -> dict:
    q = """
    query($owner: String!, $number: Int!) {
      user(login: $owner) {
        projectV2(number: $number) {
          id
          fields(first: 40) {
            nodes {
              ... on ProjectV2Field { id name }
              ... on ProjectV2SingleSelectField { id name options { id name } }
            }
          }
          items(first: 100) {
            pageInfo { hasNextPage endCursor }
            nodes { id content { ... on Issue { number } } }
          }
        }
      }
    }"""
    d = graphql(q, owner=OWNER, number=PROJECT_NUMBER)["data"]["user"]["projectV2"]
    items = {n["content"]["number"]: n["id"] for n in d["items"]["nodes"] if n.get("content")}
    cursor = d["items"]["pageInfo"]["endCursor"]
    while d["items"]["pageInfo"]["hasNextPage"]:
        pq = """
        query($owner: String!, $number: Int!, $cursor: String!) {
          user(login: $owner) {
            projectV2(number: $number) {
              items(first: 100, after: $cursor) {
                pageInfo { hasNextPage endCursor }
                nodes { id content { ... on Issue { number } } }
              }
            }
          }
        }"""
        page = graphql(pq, owner=OWNER, number=PROJECT_NUMBER, cursor=cursor)
        d["items"] = page["data"]["user"]["projectV2"]["items"]
        items |= {n["content"]["number"]: n["id"] for n in d["items"]["nodes"] if n.get("content")}
        cursor = d["items"]["pageInfo"]["endCursor"]

    fields = {f["name"]: f for f in d["fields"]["nodes"] if f}
    return {"id": d["id"], "fields": fields, "items": items}


def add_missing(project_id: str, issues: dict[int, dict], existing: dict[int, str]) -> dict[int, str]:
    todo = [(n, i["node_id"]) for n, i in issues.items() if n not in existing]
    if not todo:
        return existing
    print(f"adding {len(todo)} issues to the project")
    if DRY:
        return existing

    def one(pair):
        n, node = pair
        q = """
        mutation($p: ID!, $c: ID!) {
          addProjectV2ItemById(input: {projectId: $p, contentId: $c}) { item { id } }
        }"""
        return n, graphql(q, p=project_id, c=node)["data"]["addProjectV2ItemById"]["item"]["id"]

    # Adding items is serialised: the Projects API rejects concurrent inserts
    # into the same project with a "temporary conflict".
    added = {}
    for i, pair in enumerate(todo, 1):
        n, item_id = one(pair)
        added[n] = item_id
        if i % 25 == 0:
            print(f"  {i}/{len(todo)}")
    return existing | added


MUT_NUM = """mutation($p:ID!,$i:ID!,$f:ID!,$v:Float!){
  updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{number:$v}}){projectV2Item{id}}}"""
MUT_SEL = """mutation($p:ID!,$i:ID!,$f:ID!,$v:String!){
  updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{singleSelectOptionId:$v}}){projectV2Item{id}}}"""


def current_values(project_id: str) -> dict[str, dict[str, str]]:
    """item id -> {field name: rendered value}, so we only write real changes."""
    out: dict[str, dict[str, str]] = {}
    cursor, has_next = None, True
    while has_next:
        q = """
        query($owner: String!, $number: Int!, $cursor: String) {
          user(login: $owner) {
            projectV2(number: $number) {
              items(first: 100, after: $cursor) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  id
                  fieldValues(first: 20) {
                    nodes {
                      ... on ProjectV2ItemFieldNumberValue {
                        number
                        field { ... on ProjectV2Field { name } }
                      }
                      ... on ProjectV2ItemFieldSingleSelectValue {
                        name
                        field { ... on ProjectV2SingleSelectField { name } }
                      }
                    }
                  }
                }
              }
            }
          }
        }"""
        kw = {"owner": OWNER, "number": PROJECT_NUMBER}
        if cursor:
            kw["cursor"] = cursor
        page = graphql(q, **kw)["data"]["user"]["projectV2"]["items"]
        for node in page["nodes"]:
            vals = {}
            for fv in node["fieldValues"]["nodes"]:
                if not fv or not fv.get("field"):
                    continue
                vals[fv["field"]["name"]] = fv.get("name") or fv.get("number")
            out[node["id"]] = vals
        has_next, cursor = page["pageInfo"]["hasNextPage"], page["pageInfo"]["endCursor"]
    return out


def main() -> None:
    issues = fetch_issues()
    numbers = sorted(issues)
    open_set = {n for n in numbers if issues[n]["state"] == "open"}
    print(f"{len(issues)} issues, {len(open_set)} open")

    blockers = fetch_blockers(numbers)
    if WIRE:
        blockers = wire_declared(issues, blockers)
    open_blockers = {n: [b for b in bs if b in open_set] for n, bs in blockers.items()}
    unlocks = transitive_unlocks(blockers, open_set)
    crit = critical_path(blockers, open_set)

    ctx = project_context()
    items = add_missing(ctx["id"], issues, ctx["items"])
    if DRY:
        ready = sorted(n for n in open_set if not open_blockers.get(n))
        print(f"\nready now ({len(ready)}), by unlocks:")
        for n in sorted(ready, key=lambda x: -unlocks.get(x, 0))[:25]:
            star = "*" if n in crit else " "
            print(f" {star} #{n:<4} unlocks {unlocks.get(n,0):>3}  {issues[n]['title']}")
        return

    f = ctx["fields"]
    status_opts = {o["name"]: o["id"] for o in f["Status"]["options"]}
    crit_opts = {o["name"]: o["id"] for o in f["Critical path"]["options"]}
    seen = current_values(ctx["id"])

    writes = []
    for n in numbers:
        item = items.get(n)
        if not item:
            continue
        cur = seen.get(item, {})
        if issues[n]["state"] == "closed":
            want_status = "Done"
        elif open_blockers.get(n):
            want_status = "Blocked"
        else:
            want_status = "Ready"
        # never clobber a hand-set working state
        if cur.get("Status") in ("In progress", "In review") and want_status != "Done":
            want_status = cur["Status"]
        if cur.get("Status") != want_status:
            writes.append((MUT_SEL, item, f["Status"]["id"], status_opts[want_status]))

        for name, value in (("Blockers", len(open_blockers.get(n, []))),
                            ("Unlocks", unlocks.get(n, 0))):
            if cur.get(name) != value:
                writes.append((MUT_NUM, item, f[name]["id"], value))

        want_crit = "On critical path" if n in crit else "Off"
        if cur.get("Critical path") != want_crit:
            writes.append((MUT_SEL, item, f["Critical path"]["id"], crit_opts[want_crit]))

    print(f"{len(writes)} field writes")

    def write(w):
        mut, item, field, value = w
        graphql(mut, p=ctx["id"], i=item, f=field, v=value)

    with ThreadPoolExecutor(3) as ex:
        list(ex.map(write, writes))
    print("done")


if __name__ == "__main__":
    main()
