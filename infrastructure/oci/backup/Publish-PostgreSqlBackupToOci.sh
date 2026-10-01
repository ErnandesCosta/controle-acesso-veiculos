#!/usr/bin/env bash

set -Eeuo pipefail

usage() {
  cat <<'EOF'
Usage:
  Publish-PostgreSqlBackupToOci.sh \
    --backup /protected/path/controle-acesso-<timestamp>.dump \
    --bucket <private-bucket-name> \
    --compartment-id <monitoring-compartment-ocid>

The matching <backup>.manifest.json file is required. Authentication always uses
the OCI compute instance principal; user profiles and API keys are not accepted.
EOF
}

backup_path=''
bucket_name=''
compartment_id=''

while (($# > 0)); do
  case "$1" in
    --backup)
      backup_path="${2:-}"
      shift 2
      ;;
    --bucket)
      bucket_name="${2:-}"
      shift 2
      ;;
    --compartment-id)
      compartment_id="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$backup_path" || -z "$bucket_name" || -z "$compartment_id" ]]; then
  usage >&2
  exit 2
fi

for command_name in oci sha256sum stat; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command was not found: $command_name" >&2
    exit 1
  fi
done

if command -v python3 >/dev/null 2>&1 && python3 --version >/dev/null 2>&1; then
  python_command='python3'
elif command -v python >/dev/null 2>&1 && python --version >/dev/null 2>&1; then
  python_command='python'
else
  echo 'Required command was not found: Python 3' >&2
  exit 1
fi

if [[ ! -f "$backup_path" || -L "$backup_path" ]]; then
  echo 'Backup must be a regular file and must not be a symbolic link.' >&2
  exit 1
fi

manifest_path="${backup_path}.manifest.json"
if [[ ! -f "$manifest_path" || -L "$manifest_path" ]]; then
  echo 'The matching integrity manifest is missing or is a symbolic link.' >&2
  exit 1
fi

mapfile -t manifest_values < <(
  # Native Windows Python writes CRLF even when invoked from Git Bash.
  # mapfile removes LF only, so normalize CR to keep the script portable.
  "$python_command" - "$manifest_path" <<'PY' | tr -d '\r'
import json
import pathlib
import re
import sys

manifest_path = pathlib.Path(sys.argv[1])
try:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
except (OSError, UnicodeError, json.JSONDecodeError) as error:
    raise SystemExit(f"Invalid UTF-8 JSON manifest: {error}") from error

required = {
    "formatVersion",
    "algorithm",
    "backupFile",
    "sizeBytes",
    "sha256",
    "createdAtUtc",
}
missing = sorted(required.difference(manifest))
if missing:
    raise SystemExit(f"Manifest is missing required properties: {', '.join(missing)}")
if manifest["formatVersion"] != 1 or manifest["algorithm"] != "SHA-256":
    raise SystemExit("Unsupported manifest format or integrity algorithm.")
if not isinstance(manifest["sizeBytes"], int) or manifest["sizeBytes"] <= 0:
    raise SystemExit("Manifest sizeBytes must be a positive integer.")
if not re.fullmatch(r"[0-9a-fA-F]{64}", str(manifest["sha256"])):
    raise SystemExit("Manifest SHA-256 value is invalid.")

print(manifest["backupFile"])
print(manifest["sizeBytes"])
print(str(manifest["sha256"]).lower())
print(manifest["createdAtUtc"])
PY
)

if ((${#manifest_values[@]} != 4)); then
  echo 'The integrity manifest could not be validated.' >&2
  exit 1
fi

backup_file_name="$(basename -- "$backup_path")"
if [[ "${manifest_values[0]}" != "$backup_file_name" ]]; then
  echo 'The manifest does not refer to the selected backup file.' >&2
  exit 1
fi

actual_size="$(stat --format='%s' -- "$backup_path")"
if [[ "${manifest_values[1]}" != "$actual_size" ]]; then
  echo 'Backup size does not match the integrity manifest.' >&2
  exit 1
fi

actual_sha256="$(sha256sum -- "$backup_path" | awk '{print tolower($1)}')"
if [[ "${manifest_values[2]}" != "$actual_sha256" ]]; then
  echo 'Backup SHA-256 does not match the integrity manifest.' >&2
  exit 1
fi

created_date="${manifest_values[3]:0:10}"
if [[ ! "$created_date" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
  echo 'Manifest createdAtUtc must begin with an ISO-8601 date.' >&2
  exit 1
fi

run_id="$("$python_command" -c 'import uuid; print(uuid.uuid4().hex)')"
object_prefix="postgresql/${created_date//-//}/$run_id"
backup_object="$object_prefix/$backup_file_name"
manifest_object="$object_prefix/$(basename -- "$manifest_path")"
metric_file="$(mktemp)"
publication_succeeded=false

publish_metric() {
  local metric_name="$1"
  local metric_value="$2"

  METRIC_COMPARTMENT_ID="$compartment_id" \
  METRIC_BUCKET_NAME="$bucket_name" \
  METRIC_NAME="$metric_name" \
  METRIC_VALUE="$metric_value" \
    "$python_command" - "$metric_file" <<'PY'
import datetime
import json
import os
import pathlib
import sys

payload = [{
    "namespace": "controle_acesso_veiculos",
    "compartmentId": os.environ["METRIC_COMPARTMENT_ID"],
    "name": os.environ["METRIC_NAME"],
    "dimensions": {"bucketName": os.environ["METRIC_BUCKET_NAME"]},
    "datapoints": [{
        "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "value": float(os.environ["METRIC_VALUE"]),
        "count": 1,
    }],
}]
pathlib.Path(sys.argv[1]).write_text(json.dumps(payload), encoding="utf-8")
PY

  oci monitoring metric-data post \
    --auth instance_principal \
    --metric-data "file://$metric_file" \
    --output json >/dev/null
}

cleanup() {
  local exit_code=$?
  set +e

  if [[ "$publication_succeeded" != true ]]; then
    publish_metric BackupFailure 1 >/dev/null 2>&1 ||
      echo 'Warning: the backup failed and the failure metric could not be published.' >&2
  fi

  rm -f -- "$metric_file"
  exit "$exit_code"
}
trap cleanup EXIT

oci os object put \
  --auth instance_principal \
  --bucket-name "$bucket_name" \
  --name "$manifest_object" \
  --file "$manifest_path" \
  --content-type 'application/json' \
  --no-overwrite \
  --verify-checksum \
  --output json >/dev/null

oci os object put \
  --auth instance_principal \
  --bucket-name "$bucket_name" \
  --name "$backup_object" \
  --file "$backup_path" \
  --content-type 'application/octet-stream' \
  --no-overwrite \
  --verify-checksum \
  --output json >/dev/null

remote_size="$(
  oci os object head \
    --auth instance_principal \
    --bucket-name "$bucket_name" \
    --name "$backup_object" \
    --query 'data."content-length"' \
    --raw-output
)"

if [[ "$remote_size" != "$actual_size" ]]; then
  echo 'Uploaded backup size does not match the local artifact.' >&2
  exit 1
fi

publish_metric BackupSuccess 1
publication_succeeded=true

echo "Protected backup and manifest published under object prefix: $object_prefix"
echo 'Upload checksum and remote object size were verified.'
