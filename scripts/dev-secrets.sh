#!/bin/sh
# Seeds OBVIOUS placeholder values into dotnet user-secrets so a fresh clone boots
# without ceremony (ticket 0.7 makes the Worker refuse to start without them).
#
# Placeholders live HERE — in the per-user secrets store — and never in the committed
# appsettings.json, which stays empty by design: ValidateOnStart is the guarantee that a
# production host missing a real secret fails its deploy at boot, and a committed
# placeholder would let it boot "healthy" instead. AppSettingsStructureTests enforces that.
#
# The values are self-describing on purpose: if one ever reaches a real API or a log,
# it names itself as fake. Replace them when the consuming feature lands (Epic 4 for the
# AI key, corpus ingestion for the token):
#
#   dotnet user-secrets set "Whetstone:Ai:ApiKey" "<real value>" --project src/Whetstone.Worker
#
# Re-running is safe: a key that is already set is left alone.
set -eu
cd "$(dirname "$0")/.."

seed() {
    key="$1"
    value="$2"

    if dotnet user-secrets list --project src/Whetstone.Worker | grep -q "^$key "; then
        echo "dev-secrets: $key already set — leaving it alone"
    else
        dotnet user-secrets set "$key" "$value" --project src/Whetstone.Worker >/dev/null
        echo "dev-secrets: $key seeded with a placeholder"
    fi
}

seed "Whetstone:Ai:ApiKey" "PLACEHOLDER-whetstone-dev-ai-key-replace-before-epic-4"
seed "Whetstone:Corpus:RepoAccessToken" "PLACEHOLDER-whetstone-dev-corpus-token-replace-before-ingestion"
