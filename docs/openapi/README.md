# OpenAPI snapshot

One document is committed here, and it is the source of truth every API page in
[../api/](../api/) was written against:

| File | Content |
| --- | --- |
| [llm.json](llm.json) | `ballware LLM user API` — 60 paths, 15 component schemas, OpenAPI `3.0.4` |
| [manifest.json](manifest.json) | Provenance: generator tool + version, environment, git describe reference (`-dirty` suffix while the working tree has uncommitted changes), `fetchedAt`, per-document openapi/title/pathCount/schemaCount |
| [fetch-spec.sh](fetch-spec.sh) | Generator script |

## Only `llm` is exported

`Startup.cs:170-199` declares **two** Swagger documents:

- `llm` — "ballware LLM user API" (`Startup.cs:172-176`)
- `service` — "ballware LLM service API" (`Startup.cs:178-182`)

At runtime both are served (`Startup.cs:259-275`), but **no endpoint is assigned to the `service`
group** — every mount passes `apiGroup = "llm"` (`Startup.cs:245-254`, and
`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:19` defaults
to `meta` which the LLM mount overrides). Exporting `service` would commit a document with an empty
`paths` object, so it is not exported. `../api/README.md` states this explicitly so downstream
aggregation does not create a section for a snapshot that does not exist.

Live check (running instance): `GET /swagger/service/swagger.json` returns a document whose `paths`
object is empty.

## How the snapshot is produced

Generation runs **offline from the built assembly** — no database, no IdP:

```bash
docs/openapi/fetch-spec.sh
```

What it does:

1. `dotnet build src/Ballware.Llm.Service --configuration Release`
2. Ensures the pinned tool exists in an isolated tool path:
   `dotnet tool install Swashbuckle.AspNetCore.Cli --version 10.1.0 --tool-path "$TMPDIR/ballware-llm-service-swagger-cli-10.1.0"`
   (override the directory with `SWASHBUCKLE_CLI_TOOL_PATH`)
3. Exports the document with the Swagger environment:

   ```bash
   export ASPNETCORE_ENVIRONMENT=Swagger
   export Authorization__Authority=https://localhost
   cd src/Ballware.Llm.Service
   "$SWAGGER_BIN" tofile --output "$REPO_ROOT/docs/openapi/llm.json" bin/Release/net10.0/Ballware.Llm.Service.dll llm
   ```

4. Validates the file (parses as JSON, `openapi` starts with `3.`, `paths` non-empty, path count
   within 25 % of the expected 60) and writes `manifest.json`.

`swagger tofile` resolves the host from the assembly **without starting it**, so the EF
initialization worker (`src/Ballware.Llm.Data.Ef.Postgres/Internal/InitializationWorker.cs`) never
runs — no Postgres, no IdP needed.

## Tool version pin — and why the global tool must be ignored

`Swashbuckle.AspNetCore.Cli` must match the Swashbuckle packages referenced by the service, because
the CLI loads the service assembly into the same context: `Swashbuckle.AspNetCore` and
`Swashbuckle.AspNetCore.Annotations` are pinned to **10.1.0**
(`src/Ballware.Llm.Service/Ballware.Llm.Service.csproj:29-30`), which resolves
`Microsoft.OpenApi` **2.3.0**. A CLI built against a newer `Microsoft.OpenApi` fails immediately:

```text
Unhandled exception. System.IO.FileNotFoundException: Could not load file or assembly
'Microsoft.OpenApi, Version=2.7.5.0, ...'
   at Swashbuckle.AspNetCore.Cli.Program.<>c.<Main>b__1_5(IDictionary`2 namedArgs)
```

(observed with a globally installed `swashbuckle.aspnetcore.cli` 10.3.0). The script therefore never
uses a global `swagger` binary and installs the pinned version into its own tool path; it prints a
note when the global tool version differs. Other versions may also reformat the document and create
spurious diffs.

> **CI divergence:** `.github/workflows/publish-packages.yml:32` installs
> `dotnet tool update Swashbuckle.AspNetCore.Cli --version 7.2.0`. That pin does **not** match this
> service's Swashbuckle packages and is not used by this folder; the CI step that would export the
> spec (`Generate Swagger specification`, `publish-packages.yml:50-59`) is disabled with `if: false`
> and targets the `service` document. Do not reuse 7.2.0 for these snapshots.

## Why `ASPNETCORE_ENVIRONMENT=Swagger`

`src/Ballware.Llm.Service/appsettings.Swagger.json` supplies **fake** values for every required
section: `Authorization` (`Authority: https://localhost`, `Audience: fakeAudience`), `Swagger`
(`ClientId: fake`), `Inference`/`Embedding` (`http://localhost:11434`, `fake_api_key`), `Docling`
(`http://localhost:5007`), `MetaClient`/`StorageClient` (`https://fake`), Quartz `RAMJobStore`, and a
localhost connection string. Consequently the `oidc` security definition points at a non-existent
endpoint:

```json
"oidc": { "type": "openIdConnect", "openIdConnectUrl": "https://localhost/.well-known/openid-configuration" }
```

That URL is a placeholder, **not** a real IdP discovery endpoint. Do not wire clients to it. The
snapshot's global security requirement is `[ { "oidc": [ "openid", "mlApi" ] } ]`, taken from the
`Swagger:RequiredScopes` default `openid mlApi` (`src/Ballware.Llm.Service/Configuration/SwaggerOptions.cs:7`)
and applied as a document-level requirement (`Startup.cs:192-198`).

> **Leak warning:** configuration order is `appsettings.json` → `appsettings.{Environment}.json` →
> `appsettings.local.json` → environment variables (`src/Ballware.Llm.Service/Program.cs:19-23`). A
> developer `appsettings.local.json` overrides the Swagger values — including the Authority — and its
> real IdP URL would be baked into the committed snapshot. The script pins
> `Authorization__Authority=https://localhost` as an environment override. Both local files are
> git-ignored (`.gitignore:492` `appsettings*.local.json`, plus the explicit App entry at
> `.gitignore:432`), but they exist on a developer machine and are copied to the output directory by
> `Ballware.Llm.Service.csproj:41-43`, so regenerate from a clean checkout when in doubt.

## Binding gate — recorded outcome

`AuthorizationOptions` uses C# `required` members that all carry defaults
(`src/Ballware.Llm.Service/Configuration/AuthorizationOptions.cs:16-23`: `TenantClaim = "tenant"`,
`UserIdClaim = sub`, `RightClaim = "right"`), while `appsettings.Swagger.json` sets only
`Authority`, `Audience`, `RequiredScopes`. **No failure occurred:** `swagger tofile` binds and
validates the options successfully, so `TenantClaim`, `UserIdClaim` and `RightClaim` did **not** need
to be added to `appsettings.Swagger.json`. No source or configuration change was made.

## Refresh cadence and drift

The snapshot is a manual artifact: regenerate after endpoint changes (new/renamed routes, DTO
changes, Swagger config). A re-run should only change `manifest.json`'s `fetchedAt`; if `llm.json`
also changes, review the diff — it is an API surface change and the pages in [../api/](../api/)
should be reviewed against it. Regenerating does not require touching any source code.

`manifest.json`'s `serviceVersion` is a `git describe` reference, so it gains a `-dirty` suffix while
the working tree has uncommitted changes (for example while this docs package is still uncommitted).
Generate the committed snapshot from a clean checkout to keep the recorded version stable.

## Snapshot ↔ docs mapping

| Snapshot | Documented by |
| --- | --- |
| `llm.json` paths | [../api/generic-crud.md](../api/generic-crud.md) (55 generic operations), [../api/llm-api-resources.md](../api/llm-api-resources.md) (5 specific operations) |
| `llm.json` schemas | [../api/schemas.md](../api/schemas.md) |
| `llm.json` paths + schemas | [../api/examples.md](../api/examples.md) (every example path exists in the snapshot) |
| not in the snapshot | [../api/signalr-chat-hub.md](../api/signalr-chat-hub.md) — the SignalR hub is outside OpenAPI |

## Live-server fallback

If `swagger tofile` ever fails to resolve the minimal-API entry point, export from a running instance
instead. This needs Postgres **and** the full configuration, because `appsettings.json`
(`src/Ballware.Llm.Service/appsettings.json`) carries no `Authorization`, `Llm`, `Inference`,
`Embedding`, `MetaClient`, `StorageClient` or `Docling` section — those must come from
`appsettings.local.json` or environment variables, otherwise the host exits with
`ConfigurationException` (`Program.cs:29-37`):

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Ballware.Llm.Service
curl -s http://localhost:5006/swagger/llm/swagger.json -o docs/openapi/llm.json
```

(`http://localhost:5006` is the Development Kestrel endpoint,
`src/Ballware.Llm.Service/appsettings.Development.json:15-21`.) Prefer the offline path: a
live-server export inherits the developer's local configuration, including a real IdP URL.

If neither path works, do **not** hand-fabricate a spec — mark `llm.json` as pending and derive the
API reference from the source instead.
