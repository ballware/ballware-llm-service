# ballware LLM Service — documentation package plan

## Goal

Add a `docs/` folder to `ballware-llm-service` that documents the service from the backend side —
API surface, request/response payloads, data model, RAG/context pipeline, jobs, and upstream
dependencies — derived from the committed OpenAPI snapshot plus the source code. Rewrite the
repository `README.md` in English, keeping it as setup/overview and delegating detail to `docs/`.
Everything published (README + docs) is in English.

The layout mirrors `ballware-meta-service/docs/` so that `ballware-docs` can later aggregate service
docs consistently.

## Decisions (confirmed with the user)

| Topic | Decision |
|---|---|
| Layout | Mirror `ballware-meta-service/docs/`: `docs/README.md` + `docs/concepts/` + `docs/api/` + `docs/openapi/` |
| OpenAPI sourcing | Offline generation from the built assembly via `Swashbuckle.AspNetCore.Cli swagger tofile`, `ASPNETCORE_ENVIRONMENT=Swagger`, no DB/IdP |
| Swagger documents | Export **`llm.json` only**; state in text that the `service` document is declared (`Startup.cs:178-182`) but currently maps **no** operations |
| Tenant metadata (`ballware-fwpro`) | Use only as research input; publish **derived, anonymized best-practice templates** — never verbatim tenant data |
| README | English rewrite; setup/architecture/quick facts stay, deep API and data-model detail moves to `docs/` |

## Verified context (read from the repo, drives content accuracy)

- Composition root `src/Ballware.Llm.Service/Startup.cs`; shared hosting in `Ballware.Llm.Hosting`
  (`LlmHostingExtensions.cs`, `AddBallwareLlm*` extensions). README currently omits `Ballware.Llm.Hosting`
  and `Ballware.Llm.App`.
- Swagger documents: `llm` = "ballware LLM user API", `service` = "ballware LLM service API"
  (`Startup.cs:170-199`); raw documents at `/swagger/llm/swagger.json` and `/swagger/service/swagger.json`
  (`Startup.cs:261-275`); `oidc` security scheme points at `Authority + /.well-known/openid-configuration`.
- JSON bodies are **PascalCase** (`PropertyNamingPolicy = null`, `Startup.cs:121-129`).
- Policies: `mlApi` ← `Authorization:RequiredMetaScope` (default `mlApi`), `serviceApi` ← `RequiredServiceScope`
  (`Startup.cs:105-119`, `Configuration/AuthorizationOptions.cs:26-27`). The `serviceApi` policy is defined
  but **no endpoint uses it** — the "service" API is a reserved placeholder.
- Concrete endpoints (`Startup.cs:244-254`):
  - `MapChatEndpoint("ai/hubs")` → SignalR hub `/ai/hubs/chat` (not in OpenAPI)
  - `MapOpenAiChatCompletionsEndpoint("/ai/v1")` → `POST /ai/v1/chat/completions` (`RequireAuthorization()` default policy)
  - `POST /ai/context/simulatesystemprompt` (`ContextEndpoint.cs:25-33`)
  - `POST /ai/origin/addfromattachment`, `POST /ai/origin/createembedding` (`OriginEndpoint.cs:24-42`)
  - `POST /ai/embedding/recreate` (`EmbeddingEndpoint.cs:24-33`)
  - `MapTenantableEditingApi` for `Context`, `Origin`, `Embedding`, `Chunk`, `Image` on `/ai/{context|origin|embedding|chunk|image}`
    → **11 operations each, 55 paths total** (`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:22-132`):
    `GET /all`, `GET /query`, `GET /new`, `GET /byid`, `POST /save`, `POST /savebatch`, `DELETE /remove/{id}`,
    `POST /import`, `GET /export`, `POST /exporturl`, `GET /download/{tenantId}/{id}` (anonymous)
  - Expected `llm.json` size ≈ **60 paths** (5 specific + 55 generic) — verify against the snapshot.
- Chat payload shapes: `OpenAiChatCompletionRequest/Response/StreamResponse` in `src/Ballware.Llm.Api/Model/`;
  `model` is the **context identifier** (`ChatEndpoint.cs:52-56,73`); messages are flattened into one prompt,
  non-`user` roles are prefixed `role: content` (`ChatEndpoint.cs:112-119`); SSE emits a role chunk, content
  deltas, a `finish_reason: "stop"` chunk, then `data: [DONE]` (`ChatEndpoint.cs:144-235`).
- SignalR contract (`ChatHub.cs`): `[Authorize]`, token also accepted via `?access_token=` for `/ai/hubs`
  (`Startup.cs:87-102`); client method `Prompt(string)`; server events `Response` (one per streamed token,
  plus a welcome message on connect) and `Error`; hub fixes context identifier `"global"` (`ChatHub.cs:34`).
- **README corrections required** (current text is stale in two places):
  - `OriginEndpoint` declares `Produces(201)` but returns `Results.Accepted()` → actual **202** for
    `addfromattachment`/`createembedding`; `EmbeddingRecreate` returns `Results.Created()` → **201**.
  - "SignalR sends a single `Response` event per prompt" is outdated: `ChatHub.Prompt` streams
    (`ChatHub.cs:71-74`, `Ballware.Llm.Inference.OpenAi/Internal/AiChatSession.cs:128`).
  - README table lists `Ballware.Llm.Service` as the only host and omits `Ballware.Llm.Hosting`/`Ballware.Llm.App`.
  - Model file name `EmbeddingRecreateRequest.cs` contains class `RecreateEmbeddingRequest` — use the class name.
- Out-of-solution directories: `src/Ballware.Assistant*`, `src/Ballware.Llm.Engine.Mcp` contain only build
  artifacts (`bin`/`obj`, no `.csproj`) and are not in `ballware-llm-service.sln` → exclude from docs; note
  them once as dead folders if a cleanup note is wanted.
- Upstream Docling Serve spec already committed: `src/Ballware.Llm.Docling.Client/openapi.json`
  (Docling Serve 1.14.2, OpenAPI 3.1.0) — reference it, do not copy it into `docs/openapi/`.
- Payload offloading: `Origin.Content` and image binaries live in the Storage Service as attachment
  `payload<extension>` (identifier `"primary"`) — `Ballware.Llm.Hosting/Extensions/OriginPayloadRepositoryHook.cs`,
  `ImagePayloadRepositoryHook.cs`.
- Tenant metadata research (`/Users/frank/Documents/GitHub/ballware-fwpro`): entities `ai.chunk`,
  `ai.context`, `ai.embedding`, `ai.origin` with rights `ai.<entity>.{add,edit,view,delete}`; `ai.origin`
  carries a custom function `createembedding` and a `CustomScripts.extendedRightsCheck` that grants
  `createembedding` when `ai.origin.edit` is present. The service maps `CustomScripts.ExtendedRightsCheck`
  → `EntityMetadata.RightsCheckScript` (`Ballware.Llm.Hosting/Mappings/MetaServiceLlmMetadataProfile.cs:10-12`).
  Use this to write **synthetic** best-practice templates only.

## Target file tree

```
docs/
  README.md                         # index, quick facts, request-flow diagram, links
  concepts/
    architecture.md                 # projects, composition root, request pipeline, error contract (ProblemDetails), ports
    tenancy-and-authorization.md    # OIDC/JWT, scope→policy, claims, Jint rights check via Meta Service
    data-model.md                   # context/origin/embedding/chunk/image tables, pgvector(768), cascade rules
    payload-offloading.md           # Storage Service attachment contract ("primary", payload<ext>)
    knowledge-pipeline.md           # Docling ingestion → chunking → embedding → image vectors (job-driven)
    rag-context-and-tools.md        # system-prompt composition, {GlobalContext}/{InitialPrompt}, top_k, tool + MCP registry
    background-jobs.md              # Quartz job keys, payloads (JobDataMap), Meta Service job mirroring
    chat-application.md             # Ballware.Llm.App host: OIDC login, /chat/{contextIdentifier}, DxAIChat, differences vs. Service
    upstream-dependencies.md        # Meta/Storage/Docling/OpenAI-compatible endpoints; link to src/Ballware.Llm.Docling.Client/openapi.json
  api/
    README.md                       # two Swagger docs, llm-only snapshot, service doc = reserved placeholder, conventions (PascalCase, auth, error shape)
    generic-crud.md                 # the 11 shared editing operations + query/import/export semantics
    llm-api-resources.md            # chat completions, simulate system prompt, origin/embedding job triggers
    signalr-chat-hub.md             # hub contract outside OpenAPI (Prompt/Response/Error, access_token query param)
    schemas.md                      # field tables for every component schema of llm.json
    examples.md                     # copy-pasteable sequences incl. SSE transcript and job round-trip
    metadata-templates.md           # derived, anonymized best-practice templates for tenant metadata (rights, extendedRightsCheck, custom function)
  openapi/
    README.md                       # how llm.json is generated/refreshed, leak warning, tool pin, live-server fallback
    llm.json                        # committed snapshot
    manifest.json                   # provenance: tool + version, environment, git describe, fetchedAt, pathCount/schemaCount
    fetch-spec.sh                   # generator script
```

## Task order

1. **Generate the snapshot first** (`docs/openapi/fetch-spec.sh` + `docs/openapi/README.md`), adapted from
   `ballware-meta-service/docs/openapi/fetch-spec.sh`:
   - `dotnet build src/Ballware.Llm.Service --configuration Release`
   - install/verify `Swashbuckle.AspNetCore.Cli` pinned to **10.1.0** (matches
     `Swashbuckle.AspNetCore` 10.1.0 in `Ballware.Llm.Service.csproj:29-30`; CI currently pins 7.2.0 in
     `.github/workflows/publish-packages.yml:32` — do not reuse that version, note the divergence in
     `docs/openapi/README.md`)
   - `ASPNETCORE_ENVIRONMENT=Swagger` and `export Authorization__Authority=https://localhost` (environment
     override beats `appsettings.local.json`, mirroring the meta-service leak warning)
   - `cd src/Ballware.Llm.Service && swagger tofile --output "$REPO_ROOT/docs/openapi/llm.json" bin/Release/net10.0/Ballware.Llm.Service.dll llm`
   - validate: parses as JSON, `openapi` starts with `3.`, `paths` non-empty, `pathCount ≈ 60`; write `manifest.json`
   - **risk/verification gate:** `AuthorizationOptions` uses C# `required` members with defaults while
     `appsettings.Swagger.json` sets only `Authority`/`Audience`/`RequiredScopes`. If binding/validation fails
     during `swagger tofile`, add `TenantClaim`, `UserIdClaim`, `RightClaim` to `appsettings.Swagger.json`
     (config-only change, no behavioral impact) and re-run; record the outcome in `docs/openapi/README.md`.
   - record that `/swagger/service/swagger.json` exists at runtime but resolves to an empty path set.
2. **`docs/README.md`** — index table, quick facts (ports: Kestrel `http://localhost:5006` in Development,
   `EXPOSE 5000/5001` in Docker; auth scopes; PascalCase; snapshot pointer), and an ASCII request-flow
   diagram in the style of `ballware-meta-service/docs/README.md:47-74`.
3. **`docs/api/` pages** — derive operations and payload tables from `docs/openapi/llm.json`; keep the 11
   generic operations described once in `generic-crud.md` and reference them from `llm-api-resources.md`.
   Match platform terminology already used in `ballware-docs/api/generic/*.mdx` (all/query/new/byid/save/
   savebatch/remove/import/export/exporturl/download) so aggregation later is mechanical.
   `examples.md` must only use paths and fields that exist in `llm.json`:
   - authenticate → `POST /ai/origin/addfromattachment` → poll job → `GET /ai/origin/byid` →
     `POST /ai/origin/createembedding` → `GET /ai/embedding/all`
   - `POST /ai/v1/chat/completions` non-stream and `stream: true` with a full SSE transcript
   - `POST /ai/context/simulatesystemprompt` request/response
   Include documented status codes exactly as returned (202 for origin job triggers, 201 for recreate,
   400 with plain-text/ProblemDetails reasons for chat validation).
4. **`docs/concepts/` pages** — content sources: `Startup.cs`, `Ballware.Llm.Hosting`, `Ballware.Llm.Jobs`,
   `Ballware.Llm.Context`, `Ballware.Llm.Ingestion.Docling` + `Ballware.Llm.Docling.Client`,
   `Ballware.Llm.Data.Ef.Postgres` migrations/`LlmDbContext`, `Ballware.Llm.App`. Include the actual
   Docling call sequence (`POST v1/convert/file/async` → `GET v1/status/poll/{task_id}` →
   `GET v1/result/{task_id}`) and the markdown placeholder/embedded double conversion.
5. **`docs/api/metadata-templates.md`** — synthetic templates only: rights naming
   `ai.<entity>.{add,edit,view,delete}`, an `extendedRightsCheck` snippet that maps the `createembedding`
   custom right to `ai.origin.edit`, and an origin/embedding metadata skeleton. No GUIDs, display names,
   scripts, or layout content copied from `ballware-fwpro`; add an explicit note that these are
   non-normative examples, not the productive tenant configuration.
6. **English README rewrite** — structure: title, one-paragraph purpose, architecture table (corrected:
   add `Ballware.Llm.Hosting`, `Ballware.Llm.App`; move `Ballware.Llm.Ingestion.Docling.Tests` under a
   testing row), feature summary (short), data model summary (short, links to `docs/concepts/data-model.md`),
   API overview table (routes/methods/auth/status codes, corrected 202/201), jobs summary, configuration
   table, prerequisites, build & run + Docker + CI/CD (service **and** app images), known limitations
   (corrected streaming statement, no multi-turn memory, no ANN index, token estimate), license. End with a
   "Documentation" section linking every page in `docs/`. No German left anywhere.
7. **Consistency pass** — every route, status code, field name, and config key in README + docs must match
   `llm.json` and the source; every internal link must resolve (relative links only, no anchors to
   non-existent headings).

## Validation

```bash
bash docs/openapi/fetch-spec.sh          # exits non-zero on invalid spec; prints path/schema counts
python3 -c "import json;d=json.load(open('docs/openapi/llm.json'));print(len(d['paths']),len(d['components']['schemas']))"
python3 - <<'PY'   # no German remnants + no unresolved relative links in README.md and docs/**.md
PY
dotnet build ballware-llm-service.sln -c Release    # confirms the docs work did not touch sources
```

Manual checks the implementing agent must report:
- `llm.json` path count matches the documented operation inventory (expected ≈ 60).
- `manifest.json` records `title: "ballware LLM user API"` and the OpenAPI version emitted by Swashbuckle 10.x.
- Snapshot contains no real IdP URL (security scheme must show `https://localhost/...`).
- No verbatim payload, GUID, or display name from `ballware-fwpro` appears in `docs/`.

## Risks / edge cases

- `swagger tofile` may fail to resolve the minimal-API entry point or may emit an empty `paths` for
  minimal endpoints. Fallback (documented in `docs/openapi/README.md`): run with
  `ASPNETCORE_ENVIRONMENT=Development` (needs Postgres) and curl `/swagger/llm/swagger.json`.
  If neither works, fall back to a code-derived API reference and mark `llm.json` as pending — do **not**
  hand-fabricate a spec.
- `src/Ballware.Llm.Service/appsettings.local.json` is **not** ignored by `.gitignore`
  (only the `Ballware.Llm.App` copy is, line 432). Do not commit it, and pin `Authorization__Authority`
  in the fetch script so a developer IdP URL cannot leak into the snapshot. Recommend (do not perform)
  adding the Service path to `.gitignore` as a follow-up.
- The `service` Swagger document stays empty: `api/README.md` must say so explicitly so `ballware-docs`
  does not later create a `llm-service` sidebar section from a snapshot that does not exist.
- Version drift: CI pins Swashbuckle CLI 7.2.0 while the service uses 10.1.0; the docs script pins 10.1.0
  and documents the mismatch instead of silently reformatting the snapshot.

## Out of scope

- Any source-code change beyond a possible `appsettings.Swagger.json` fallback (no endpoint regrouping, no
  `.gitignore` edit, no CI change, no client package generation).
- `ballware-docs` integration work (sidebars, `.api.mdx` generation, `scripts/fetch-openapi.mjs` service
  entry for the LLM service) — the docs shape here is what makes that step mechanical later.
- Cleanup or deletion of the orphaned `src/Ballware.Assistant*` and `src/Ballware.Llm.Engine.Mcp` folders.
- Docling Serve documentation beyond the calls this service actually makes.

## Open question (single, non-blocking)

Should `docs/` be linked from GitHub Pages immediately (a `ballware-docs` PR adding an `api/llm-service`
section)? Default assumption: **no** — this task ends at the service repository; the aggregation PR is a
separate follow-up.
