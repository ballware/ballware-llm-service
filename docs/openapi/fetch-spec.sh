#!/usr/bin/env bash
# Regenerates the OpenAPI snapshot committed in docs/openapi/ (llm.json, manifest.json).
#
# The snapshot is produced offline from the built service assembly via Swashbuckle's
# swagger-to-file tool, using ASPNETCORE_ENVIRONMENT=Swagger (fake Authority/clients,
# no database, no IdP needed). See docs/openapi/README.md for details and the live-server fallback.

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: docs/openapi/fetch-spec.sh

Regenerates docs/openapi/llm.json and docs/openapi/manifest.json.
Run from any location inside the repository; the script resolves the repository root itself.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

TOOL_PACKAGE="Swashbuckle.AspNetCore.Cli"
TOOL_VERSION="10.1.0"
CONFIG_PROJECT="src/Ballware.Llm.Service"
OUTPUT_DIR="docs/openapi"
DOCUMENTS=(llm)

step() { printf '\n==> %s\n' "$*"; }

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required but was not found on PATH." >&2
  exit 1
fi

step "Building $CONFIG_PROJECT (Release)"
dotnet build "$CONFIG_PROJECT" --configuration Release

# The CLI version must match the Swashbuckle packages referenced by the service, otherwise the
# tool's own Microsoft.OpenApi reference cannot be loaded next to the service's copy
# (FileNotFoundException for Microsoft.OpenApi). A globally installed swagger tool of any other
# version is therefore ignored: this script installs the pinned version into an isolated tool path.
TOOL_DIR="${SWASHBUCKLE_CLI_TOOL_PATH:-"${TMPDIR:-/tmp}/ballware-llm-service-swagger-cli-${TOOL_VERSION}"}"
SWAGGER_BIN="$TOOL_DIR/swagger"

step "Ensuring $TOOL_PACKAGE $TOOL_VERSION in $TOOL_DIR"
if [[ -x "$SWAGGER_BIN" ]]; then
  echo "swagger tool: $SWAGGER_BIN"
else
  dotnet tool install "$TOOL_PACKAGE" --version "$TOOL_VERSION" --tool-path "$TOOL_DIR"
  if [[ ! -x "$SWAGGER_BIN" ]]; then
    echo "swagger tool not found after install into $TOOL_DIR." >&2
    exit 1
  fi
fi
if command -v swagger >/dev/null 2>&1; then
  global_version="$(dotnet tool list --global 2>/dev/null | awk 'tolower($1)=="swashbuckle.aspnetcore.cli"{print $2}' || true)"
  if [[ -n "$global_version" && "$global_version" != "$TOOL_VERSION" ]]; then
    echo "note: ignoring global swagger $global_version (global tools are not version-matched to this service)."
  fi
fi

step "Generating documents"
# ASPNETCORE_ENVIRONMENT=Swagger loads appsettings.Swagger.json (fake Authority/clients).
# appsettings.local.json is applied after the environment file even in the Swagger
# environment, so the OIDC discovery URL in the generated document is pinned to the
# fake value below via an environment override. This keeps developer IdP URLs out of
# the committed snapshot (see docs/openapi/README.md).
export ASPNETCORE_ENVIRONMENT=Swagger
export Authorization__Authority="https://localhost"

# Run from the service project directory: the appsettings files are resolved relative
# to the content root (current working directory), like in CI. The absolute output
# path keeps the snapshot in docs/openapi/ regardless of the invocation directory.
cd "$CONFIG_PROJECT"

for doc in "${DOCUMENTS[@]}"; do
  # The "service" document is declared in Startup.cs but maps no operations; exporting it
  # would commit an empty paths object. Only "llm" is exported. See docs/api/README.md.
  "$SWAGGER_BIN" tofile --output "$REPO_ROOT/$OUTPUT_DIR/$doc.json" "bin/Release/net10.0/Ballware.Llm.Service.dll" "$doc"
done

cd "$REPO_ROOT"

step "Validating and writing manifest"
if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required for validation and manifest generation." >&2
  exit 1
fi

service_version="$(git describe --tags --always --dirty 2>/dev/null || echo unknown)"

TOOL_VERSION="$TOOL_VERSION" SERVICE_VERSION="$service_version" DOCUMENTS="${DOCUMENTS[*]}" \
EXPECTED_PATHS="60" \
python3 - "$OUTPUT_DIR" <<'PY'
import datetime
import json
import os
import sys

output_dir = sys.argv[1]
manifest = {
    "source": "Swashbuckle.AspNetCore.Cli swagger tofile",
    "toolVersion": os.environ["TOOL_VERSION"],
    "environment": "Swagger",
    "serviceVersion": os.environ["SERVICE_VERSION"],
    "fetchedAt": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "documents": {},
}

expected = int(os.environ.get("EXPECTED_PATHS", "0"))

for name in os.environ["DOCUMENTS"].split():
    path = os.path.join(output_dir, f"{name}.json")
    with open(path, encoding="utf-8") as file:
        doc = json.load(file)

    version = str(doc.get("openapi", ""))
    paths = doc.get("paths") or {}
    schemas = (doc.get("components") or {}).get("schemas") or {}

    if not version.startswith("3."):
        sys.exit(f"{path}: openapi version {version!r} does not start with '3.'")
    if not paths:
        sys.exit(f"{path}: paths is empty")

    title = (doc.get("info") or {}).get("title", "")
    manifest["documents"][name] = {
        "file": f"{name}.json",
        "openapi": version,
        "title": title,
        "pathCount": len(paths),
        "schemaCount": len(schemas),
    }
    print(f"{path}: openapi {version}, title {title!r}, paths {len(paths)}, schemas {len(schemas)}")

    if expected and abs(len(paths) - expected) > expected * 0.25:
        sys.exit(
            f"{path}: pathCount {len(paths)} deviates from the expected ~{expected} "
            "(5 specific + 55 generic editing operations); review the API surface before publishing."
        )

with open(os.path.join(output_dir, "manifest.json"), "w", encoding="utf-8") as file:
    json.dump(manifest, file, indent=2)
    file.write("\n")
PY

step "Done"
echo "Updated $OUTPUT_DIR/{llm,manifest}.json"
