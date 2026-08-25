# Triage Labels

The skills speak in terms of five canonical triage roles; this repo uses three of them. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

This repo has no `ready-for-agent` / `ready-for-human` labels. Every ticket is implemented by hand
(see the working agreement in `CLAUDE.md`), so the distinction those labels drew would be true of
every issue and is not worth recording. A skill that asks for the AFK-ready or human-required label
should be told the role does not exist here, and the issue left with the labels it has.

When a skill mentions one of the remaining roles, use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.
