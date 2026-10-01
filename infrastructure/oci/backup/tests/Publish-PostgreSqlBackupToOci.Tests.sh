#!/usr/bin/env bash

set -Eeuo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
subject="$script_directory/../Publish-PostgreSqlBackupToOci.sh"
test_directory="$(mktemp -d)"
trap 'rm -rf -- "$test_directory"' EXIT

backup_path="$test_directory/controle-acesso-20261001T120000000Z.dump"
printf 'fictitious-backup-content' >"$backup_path"
backup_size="$(stat --format='%s' -- "$backup_path")"
backup_sha="$(sha256sum -- "$backup_path" | awk '{print $1}')"

if command -v python3 >/dev/null 2>&1 && python3 --version >/dev/null 2>&1; then
  python_command='python3'
else
  python_command='python'
fi

"$python_command" - "$backup_path.manifest.json" "$backup_size" "$backup_sha" <<'PY'
import json
import pathlib
import sys

manifest = {
    "formatVersion": 1,
    "algorithm": "SHA-256",
    "backupFile": "controle-acesso-20261001T120000000Z.dump",
    "sizeBytes": int(sys.argv[2]),
    "sha256": sys.argv[3],
    "createdAtUtc": "2026-10-01T12:00:00.0000000+00:00",
}
pathlib.Path(sys.argv[1]).write_text(json.dumps(manifest), encoding="utf-8")
PY

mock_directory="$test_directory/bin"
mkdir -p -- "$mock_directory"
cat >"$mock_directory/oci" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >>"$OCI_CALL_LOG"
if [[ "$*" == "os object head "* ]]; then
  printf '%s\n' "$EXPECTED_BACKUP_SIZE"
fi
EOF
chmod +x "$mock_directory/oci"

export OCI_CALL_LOG="$test_directory/oci-calls.log"
export EXPECTED_BACKUP_SIZE="$backup_size"
PATH="$mock_directory:$PATH" "$subject" \
  --backup "$backup_path" \
  --bucket 'fictitious-private-bucket' \
  --compartment-id 'ocid1.compartment.oc1..example'

put_count="$(grep -c 'os object put' "$OCI_CALL_LOG")"
if [[ "$put_count" != 2 ]]; then
  echo "Expected two uploads, found $put_count." >&2
  exit 1
fi

grep -q -- '--auth instance_principal' "$OCI_CALL_LOG"
grep -q -- '--no-overwrite' "$OCI_CALL_LOG"
grep -q -- '--verify-checksum' "$OCI_CALL_LOG"
grep -q -- 'monitoring metric-data post' "$OCI_CALL_LOG"

printf 'tampered' >>"$backup_path"
if PATH="$mock_directory:$PATH" "$subject" \
  --backup "$backup_path" \
  --bucket 'fictitious-private-bucket' \
  --compartment-id 'ocid1.compartment.oc1..example'; then
  echo 'The publisher accepted a backup that did not match its manifest.' >&2
  exit 1
fi

echo 'OCI backup publisher tests passed.'
