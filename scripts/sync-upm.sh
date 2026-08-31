#!/usr/bin/env bash
#
# Syncs Licensr.Sdk's C# sources into the Unity UPM package at upm/Runtime/,
# which is otherwise-unbuildable Unity package metadata (package.json,
# .asmdef) wrapping an exact copy of the same source — no Unity-only code,
# no `#if UNITY` branches needed, mirroring how sdks/js's dist/ bundles its
# src/ unmodified.
#
# Usage:
#   scripts/sync-upm.sh          # overwrite upm/Runtime/*.cs from Licensr.Sdk/
#   scripts/sync-upm.sh --check  # fail (no writes) if the two have drifted;
#                                 # used by test-sdk-dotnet.yaml
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

SRC="Licensr.Sdk"
DEST="upm/Runtime"
CHECK=0
[ "${1:-}" = "--check" ] && CHECK=1

TARGET="$DEST"
if [ "$CHECK" = 1 ]; then
  TARGET="$(mktemp -d)"
  trap 'rm -rf "$TARGET"' EXIT
else
  mkdir -p "$DEST"
  find "$DEST" -name "*.cs" -delete
fi

while IFS= read -r -d '' file; do
  rel="${file#"$SRC"/}"
  mkdir -p "$TARGET/$(dirname "$rel")"
  cp "$file" "$TARGET/$rel"
done < <(find "$SRC" -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -print0)

if [ "$CHECK" = 1 ]; then
  if ! diff -rq --exclude="*.asmdef" --exclude="*.meta" "$TARGET" "$DEST" >/dev/null; then
    echo "upm/Runtime has drifted from Licensr.Sdk/ — run scripts/sync-upm.sh and commit the result." >&2
    diff -rq --exclude="*.asmdef" --exclude="*.meta" "$TARGET" "$DEST" || true
    exit 1
  fi
  CS_VER="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Licensr.Sdk/Licensr.Sdk.csproj | head -1)"
  UPM_VER="$(python3 -c "import json; print(json.load(open('upm/package.json'))['version'])")"
  if [ "$CS_VER" != "$UPM_VER" ]; then
    echo "Version mismatch: Licensr.Sdk.csproj is $CS_VER but upm/package.json is $UPM_VER" >&2
    exit 1
  fi
  echo "upm/Runtime is in sync with Licensr.Sdk/ (version $CS_VER)."
else
  echo "Synced $(find "$DEST" -name '*.cs' | wc -l | tr -d ' ') file(s) into $DEST/."
fi
