# ballware LLM Service

The **ballware LLM Service** is the platform-side microservice of the ballware product family that combines
LLM-backed chat with **RAG (retrieval-augmented generation)** over a tenant-scoped knowledge base. It
converts documents (via **Docling Serve**), splits them into text chunks, vectorizes them through an
**OpenAI-compatible embedding API**, and stores them in **PostgreSQL with pgvector**. At runtime a system
prompt is composed from tenant- and document-level context and handed to an OpenAI-compatible chat
endpoint together with registered tools (own knowledge tools, math tools, external **MCP** servers). A
second host in the same solution, `Ballware.Llm.App`, ships a Blazor chat UI on top of the same stack.

- Target platform: **.NET 10** (`net10.0`)
- Storage: **PostgreSQL / pgvector** (`vector(768)`), EF Core 10 + Npgsql
- Authentication: **OIDC / JWT bearer** (tenant-scoped, scope-based policies)
- Communication: HTTP minimal API (including an **OpenAI-compatible** `chat/completions` endpoint) and a
  **SignalR** chat hub
- Background processing: **Quartz.NET** (clustered, PostgreSQL job store)
- API contract: OpenAPI `3.0.4`, 60 paths — committed snapshot in [docs/openapi/llm.json](docs/openapi/llm.json)

Deep documentation lives in [docs/](docs/); this file is overview, setup and quick facts.

## Architecture overview

The solution is split into low-coupling projects: contracts in the neutral projects, implementations in the
`.OpenAi` / `.SemanticKernel` / `.Docling` / `.Postgres` projects (ports & adapters). Both hosts assemble
them through the shared `Ballware.Llm.Hosting` extension methods.

| Project | Role |
|---|---|
| `Ballware.Llm.Service` | Composition root: `Program.cs`, `Startup.cs`, configuration, JWT auth, DI wiring, Swagger, job adapters, Docker entry point |
| `Ballware.Llm.App` | Composition root: Blazor Server chat host (OIDC cookie sign-in, `DxAIChat`); no API, Swagger or jobs |
| `Ballware.Llm.Hosting` | Shared DI wiring (`AddBallwareLlm*`), Meta/Storage adapters, payload repository hooks, hosting configuration models |
| `Ballware.Llm.Api` | HTTP endpoints (minimal API), OpenAI-compatible chat, SignalR `ChatHub`, session store, request/response models |
| `Ballware.Llm.Inference` | Inference contracts: `IInferenceEngine`, `IChatSession`, `ChatContextOptions` |
| `Ballware.Llm.Inference.OpenAi` | OpenAI SDK + `Microsoft.Extensions.AI` implementation (`ChatClient` → `IChatClient`, `UseFunctionInvocation()`), tool registry, MCP client use |
| `Ballware.Llm.Embedding` | Contracts: `IEmbeddingEngine`, `IChunker<T>`, `ChunkerRegistry`, `ChunkingOptions` |
| `Ballware.Llm.Embedding.OpenAi` | Embedding engine over `EmbeddingClient` |
| `Ballware.Llm.Embedding.SemanticKernel` | `MarkdownChunker` based on `Microsoft.SemanticKernel.Text.TextChunker` |
| `Ballware.Llm.Context` | System-prompt composition, retrieval over global origins, token estimation (`SmartFormat.NET`) |
| `Ballware.Llm.Tools` | Tool contracts: `IToolRegistry`, `IToolProvider`, `Tool`, `ToolParameter`, `IMcpService` |
| `Ballware.Llm.Tools.Common` | `MathToolProvider` (`add`, `subtract`) |
| `Ballware.Llm.Tools.Embedding` | `EmbeddingsToolProvider` (`origins.list`, `origins.content`, `origins.query`) |
| `Ballware.Llm.Ingestion` | Contracts `IKnowledgeIngestor`, `IngestionResult`, `IngestionImage` |
| `Ballware.Llm.Ingestion.Docling` | `DoclingMarkdownIngestor` / `DoclingJsonIngestor`, `DoclingHttpApiClient`, `DoclingImageExtractor` |
| `Ballware.Llm.Docling.Client` | NSwag-generated Docling Serve client plus a hand-written extension for async conversion; upstream spec `openapi.json` |
| `Ballware.Llm.Data` | Domain models (`Persistables` for EF, `Public` for the API) and repository interfaces |
| `Ballware.Llm.Data.Ef` | EF repository base classes, Mapster mapping, `LlmStorageOptions`, model customizer |
| `Ballware.Llm.Data.Ef.Postgres` | `LlmDbContext`, Npgsql/pgvector configuration, vector repositories, `InitializationWorker`, migrations |
| `Ballware.Llm.Jobs` | Quartz jobs: origin ingestion, embedding creation, embedding recreation |
| `Ballware.Llm.Metadata` | `Tenant`, `EntityMetadata`, `Attachment` (authorization DTOs) |
| `Ballware.Llm.Ingestion.Docling.Tests` | **Testing**: NUnit + Testcontainers (WireMock) tests for the Docling ingestion path |

External platform dependencies: `ballware.shared.*` (API endpoints, authorization, data, Jint rights
check), `ballware.meta.service.client`, `ballware.storage.service.client`, `Ballware.Shared.Web.Oidc` /
`.Session` (App host), `Duende.AccessTokenManagement`, Serilog, Swashbuckle, Mapster.

Not part of the build: `src/Ballware.Assistant*` and `src/Ballware.Llm.Engine.Mcp` contain only `bin`/`obj`
artifacts (no `.csproj`, no sources) and are absent from `ballware-llm-service.sln`. MCP functionality lives
in `Ballware.Llm.Inference.OpenAi` + `Ballware.Llm.Tools` + `Ballware.Llm.Hosting`.

## Features

- **Knowledge ingestion** — `POST /ai/origin/addfromattachment` reads a Storage Service attachment, converts
  it through Docling Serve (`POST v1/convert/file/async` → `GET v1/status/poll/{task_id}` →
  `GET v1/result/{task_id}`, 1 s polling), stores the document as an `origin`, extracts images
  (SHA-256 deduplicated) and vectorizes their description texts.
- **Chunking and embedding** — `text/markdown` → `MarkdownChunker` (line then paragraph split, 500/50 token
  defaults); each chunk and each image text becomes a row with a `vector(768)` payload.
- **Context (RAG)** — `ModelContextProvider` collects global origins (`global_context = true`, ordered by
  `global_relevance` ascending), retrieves top-k chunks per active embedding by pgvector cosine distance,
  and applies the result to `system_prompt_template` via SmartFormat with the placeholders
  `{GlobalContext}` and `{InitialPrompt}`.
- **Chat** — OpenAI-compatible `POST /ai/v1/chat/completions` (stateless, optional SSE) and the SignalR hub
  `/ai/hubs/chat` (session-per-connection, context identifier fixed to `global`).
- **Tools and MCP** — flat tool names `add`, `subtract`, `origins.list`, `origins.content`, `origins.query`;
  external MCP servers per `Mcp.Agents[]` with optional user-token delegation.
- **Tenancy and rights** — scope policy `mlApi`, tenant/user/right claims, and Jint rights scripts supplied
  by the Meta Service.

Details: [docs/concepts/knowledge-pipeline.md](docs/concepts/knowledge-pipeline.md),
[docs/concepts/rag-context-and-tools.md](docs/concepts/rag-context-and-tools.md),
[docs/concepts/tenancy-and-authorization.md](docs/concepts/tenancy-and-authorization.md).

## Data model

All tables are tenant-scoped (`tenant_id`, `uuid`, audit columns) with an index on `tenant_id`, a unique
index on `(tenant_id, uuid)`, and **no foreign keys** — relationships are plain uuid columns.

| Table | Key columns |
|---|---|
| `context` | `identifier (100)`, `system_prompt_template`, `welcome_message`, `model (100)`, `num_ctx` — unique `(tenant_id, identifier)` |
| `origin` | `name (100)`, `description`, `content_type (100)`, `global_context`, `global_relevance`, `owner_entity/owner_id`, `refering_entity/refering_id` — unique `(tenant_id, owner_entity, owner_id, name)`; **no content column** |
| `embedding` | `origin_id`, `model`, `active`, `top_k`, `max_tokens_per_chunk`, `overlap_tokens` |
| `chunk` | `embedding_id`, `text (102400)`, `data vector(768)` |
| `image` | `origin_id`, `name (512)`, `description (4096)`, `content_type (100)`, `text (16384)`, `data vector(768)` — non-unique index `(tenant_id, origin_id)` |

Relationships: `origin.uuid → embedding.origin_id → chunk.embedding_id` and `origin.uuid → image.origin_id`.
Deleting an origin cascades to its embeddings (with their chunks) and its images **in the repositories**, not
in the database. Payload offloading: the document text of an origin and the image binaries are attachments in
the ballware **Storage Service** (file name `payload<extension>`, owner entity `origin`/`image`), not Postgres
columns.

Details: [docs/concepts/data-model.md](docs/concepts/data-model.md),
[docs/concepts/payload-offloading.md](docs/concepts/payload-offloading.md).

## API overview

Two Swagger documents are declared — `llm` ("ballware LLM user API", 60 paths, 15 schemas) and `service`
("ballware LLM service API"), which currently maps **no** operations. Swagger UI at `/swagger`, raw documents
at `/swagger/llm/swagger.json` and `/swagger/service/swagger.json`. JSON bodies are PascalCase except the
OpenAI chat payloads. See [docs/api/README.md](docs/api/README.md).

| Route | Method | Authorization | Success code | Notes |
|---|---|---|---|---|
| `/ai/hubs/chat` | SignalR | `[Authorize]`; token also accepted as `?access_token=` | — | interactive hub: `Prompt` → `Response` (one event per streamed token, plus a welcome event) / `Error`; not in OpenAPI |
| `/ai/v1/chat/completions` | POST | default policy (any authenticated user — **no** `mlApi` scope required) | `200` | OpenAI-compatible; `model` is the context identifier; `400` plain-text validation messages; `stream: true` yields SSE |
| `/ai/context/simulatesystemprompt` | POST | policy `mlApi` | `200` | dry run of prompt composition + token estimate, no model call |
| `/ai/origin/addfromattachment` | POST | policy `mlApi` | **`202`** | triggers job `("addfromattachment","origin")`; the snapshot declares `201` |
| `/ai/origin/createembedding` | POST | policy `mlApi` | **`202`** | one job per origin id; the snapshot declares `201` |
| `/ai/embedding/recreate` | POST | policy `mlApi` | `201` | one job per embedding id |
| `/ai/{context\|origin\|embedding\|chunk\|image}/…` | 11 operations each | policy `mlApi` (download is anonymous) | `200`/`201` | generic tenant editing API: `all`, `query`, `new`, `byid`, `save`, `savebatch`, `remove/{id}`, `import`, `export`, `exporturl`, `download/{tenantId}/{id}` |

Authorization: JWT bearer against `Authorization.Authority`/`Audience` (optional `Issuer`). Policies compare
the `scope` claim against `Authorization.RequiredMetaScope` (`mlApi`) or `RequiredServiceScope`
(`serviceApi`, reserved and unused). Tenant, user and rights are read through `Ballware.Shared.Authorization`
(`TenantClaim`, `UserIdClaim`, `RightClaim`); rights are evaluated script-wise (Jint) against Meta Service
metadata. `500` responses are `application/problem+json`.

Details: [docs/api/generic-crud.md](docs/api/generic-crud.md),
[docs/api/llm-api-resources.md](docs/api/llm-api-resources.md),
[docs/api/schemas.md](docs/api/schemas.md), [docs/api/examples.md](docs/api/examples.md),
[docs/api/signalr-chat-hub.md](docs/api/signalr-chat-hub.md).

## Background jobs

Quartz jobs are registered durably **without triggers** — they run only when an endpoint triggers them. Only
the import jobs mirror status into the Meta Service (`Scheduler = "document"`, `Identifier = "import"`,
states `InProgress`/`Finished`/`Error`); the three LLM jobs publish no job record, so progress is observed by
reading the data.

| Job key | Job | Purpose |
|---|---|---|
| `("addfromattachment","origin")` | `OriginAddFromAttachmentJob` | read attachment → Docling ingestion → write origin (+ images) |
| `("createembedding","origin")` | `OriginCreateEmbeddingJob` | chunk origin content → write embedding → write chunk vectors (+ image chunks) |
| `("recreate","embedding")` | `EmbeddingRecreateJob` | delete chunks, apply chunk settings, re-chunk and re-embed |
| `("import","origin")`, `("import","embedding")` | `TenantableImportJob<…>` | import via the generic editing API |

Details: [docs/concepts/background-jobs.md](docs/concepts/background-jobs.md).

## Configuration

Configuration sources: `appsettings.json` → `appsettings.{Environment}.json` → `appsettings.local.json`
(git-ignored) → environment variables (`src/Ballware.Llm.Service/Program.cs:19-23`). Missing mandatory
sections abort startup with a `ConfigurationException`. Most options are registered with
`ValidateOnStart` + data annotations.

| Section | Keys | Required |
|---|---|---|
| `Authorization` | `Authority`, `Audience`, `Issuer`, `TenantClaim` (default `tenant`), `UserIdClaim` (`sub`), `RightClaim` (`right`), `RequireHttpsMetadata`, `RequiredMetaScope` (`mlApi`), `RequiredServiceScope` (`serviceApi`) | Authority, Audience, claims |
| `Llm` | `Provider` (`postgres` is the only implemented value), `AutoMigrations` (default `false`) | `Provider` |
| `ConnectionStrings` | `LlmStorageConnection` (and a Quartz data source connection when a persistent job store is used) | yes |
| `Inference` | `Endpoint`, `ApiKey`, `Model` | Endpoint, ApiKey |
| `Embedding` | `Endpoint`, `ApiKey`, `Model` | Endpoint, ApiKey |
| `Docling` | `Url`, `OutputFormat` (`markdown` \| `json`), `RequestTimeoutSeconds` (default 120) | Url, OutputFormat |
| `Context` | `DefaultTopK` (5) | — |
| `Chunking` | `DefaultMaxTokensPerChunk` (500), `DefaultOverlapTokens` (50) | — |
| `Mcp` | `Agents[]`: `Namespace`, `ServiceUrl`, `UserTokenDelegation`, `CredentialIdentifier` | — |
| `ServiceCredentials` | `Credentials[]`: `Identifier`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scopes` | for Meta/Storage calls |
| `MetaClient` / `StorageClient` | `ServiceUrl`, `CredentialIdentifier` | yes (both sections) |
| `Swagger` | `EnableClient`, `ClientId`, `ClientSecret`, `RequiredScopes` (default `openid mlApi`) | — |
| `Cors` | `AllowedOrigins`, `AllowedMethods`, `AllowedHeaders` (comma-separated) | — |
| `Quartz` | scheduler/job-store settings (clustered PostgreSQL `JobStoreTX`) | — |
| `Serilog` / `Logging` / `Kestrel` | log levels, console sink, request-size limits | — |

`Endpoint` values in `Inference` and `Embedding` get `/v1` appended automatically when the path is empty or
does not already end in `/v1`, so OpenAI endpoints, Azure gateways and compatible servers (Ollama, vLLM,
LiteLLM) all work.

## Prerequisites

- PostgreSQL with the **`vector`** extension (pgvector). The extension is declared in `LlmDbContext`
  (`HasPostgresExtension("vector")`) but **no migration creates it**; the Quartz job-store tables are likewise
  not created by this repository.
- **Docling Serve** for document conversion.
- **ballware Meta Service** for tenant/entity metadata, rights scripts, job and export records.
- **ballware Storage Service** for origin payloads, image binaries and import/export files.
- An OIDC authority with a user client (scope `mlApi`, `tenant` claim) and client-credentials clients for
  service-to-service calls.
- NuGet feed `https://nuget.pkg.github.com/ballware/…` for the `ballware.*` packages; the App build additionally
  needs a DevExpress NuGet feed and license.

## Build & run

```bash
# Build the solution (.NET SDK 10, rollForward: latestMajor — see global.json)
dotnet build ballware-llm-service.sln -c Release

# Run the API service (Development: http://localhost:5006)
dotnet run --project src/Ballware.Llm.Service

# Run the chat app (listens on http://+:3004)
dotnet run --project src/Ballware.Llm.App

# Tests (NUnit; requires Docker for Testcontainers/WireMock)
dotnet test test/Ballware.Llm.Ingestion.Docling.Tests

# Regenerate the committed OpenAPI snapshot (offline, no database/IdP)
docs/openapi/fetch-spec.sh
```

Containers:

```bash
docker build -f src/Ballware.Llm.Service/Dockerfile src/. \
  --build-arg GITHUB_USERNAME=<user> --build-arg GITHUB_PAT=<token> \
  -t ballware-llm-service
docker run -p 5006:5000 ballware-llm-service

docker build -f src/Ballware.Llm.App/Dockerfile src/. \
  --build-arg GITHUB_USERNAME=<user> --build-arg GITHUB_PAT=<token> \
  --build-arg DEVEXPRESS_NUGETFEED=<feed> --build-arg DEVEXPRESS_LICENSE=<license> \
  -t ballware-llm-app
docker run -p 3004:3004 ballware-llm-app
```

Both Dockerfiles are multi-stage (`dotnet/aspnet:10.0` / `dotnet/sdk:10.0`), declare `EXPOSE 5000` and
`EXPOSE 5001`, run as the non-root user `app`, and start `Ballware.Llm.Service.dll` / `Ballware.Llm.App.dll`.
Note the App's `EXPOSE` values do not match its `3004` listener — publish it with an explicit port mapping.

## CI/CD

`.github/workflows/publish-packages.yml` runs on push to `main` and `develop`:

- **build_and_pack**: GitVersion SemVer, `dotnet build` of the solution, version tag `v<semver>` on `main`.
  Swagger export, NSwag client generation, `dotnet pack` and `nuget push` steps exist but are disabled with
  `if: false`; no `dotnet test` step is defined. The workflow installs `Swashbuckle.AspNetCore.Cli` **7.2.0**,
  which does **not** match this service's Swashbuckle 10.1.0 — the docs script pins 10.1.0 instead
  ([docs/openapi/README.md](docs/openapi/README.md)).
- **docker_publish**: multi-arch builds (`linux/amd64`, `linux/arm64`) pushed as
  `ghcr.io/ballware/ballware-llm-service` and `ghcr.io/ballware/ballware-llm-app` with tags `<semver>` plus
  `stable` (main) or `latest` (develop).

## Known limitations and current behavior

- **Streaming is real per transport, but the REST endpoint is stateless.** SSE sends one chunk per token
  (role chunk, content chunks, a `finish_reason: "stop"` chunk, then `data: [DONE]`), and the hub sends one
  `Response` event per token. Each HTTP request builds a new inference session, so conversation state is not
  carried over the REST API; client-supplied history is flattened into a single prompt.
- **No multi-turn memory across requests.** Hub sessions keep history only for the lifetime of one connection
  (`ChatSessionStore` is in-memory, no persistence, no TTL), and the system prompt plus retrieved knowledge are
  computed from the first message only.
- **No ANN index.** Vector search is a filtered sequential scan ordered by cosine distance; no HNSW/IVFFlat
  index exists, so cost grows linearly with chunk count.
- **Token figures are estimates.** `length / 4` for the system prompt, `(length + 3) / 4` in the chat paths —
  logging/preview only.
- **Declared vs. actual status codes.** The snapshot declares `201` for both origin job triggers, but the
  handlers return `202`; `recreate` returns `201` as declared.
- **`Embedding.top_k` is never written** by the create-embedding job (`TopK` is accepted by the request model
  but not forwarded to the job), so retrieval falls back to `Context:DefaultTopK` (5). Chunk settings are only
  persisted by the recreate job.
- **Only `text/markdown` has a chunker.** An origin whose `content_type` is something else fails the embedding
  job with `KeyNotFoundException`.
- **Import job rights quirk.** The shared import job evaluates `meta.<entity>.<identifier>`, not
  `ai.<entity>.<identifier>`, and import job keys exist only for `origin` and `embedding`.
- **Tool schema `required` is never emitted** (`ToolParameter` ignores its `required` constructor argument), and
  some model-facing tool output strings, the hub's `Error` payload, and the App's UI text are localized in
  German.
- **Single attempt, no retry** against the inference endpoint; context-length errors (HTTP 400/413) are logged
  and rethrown without truncation. MCP clients are created per chat and not disposed.
- Unused contracts: `IModelTool`/`IModelToolProvider`, `IChatSessionBuilder`/`IChatSessionBuilderStage`,
  `Ballware.Llm.Metadata.Attachment`.

## License

MIT — see [LICENSE](LICENSE). © ballware Software & Consulting

## Documentation

| Page | Topic |
|---|---|
| [docs/README.md](docs/README.md) | index, quick facts, request-flow diagram |
| [docs/concepts/architecture.md](docs/concepts/architecture.md) | projects, composition roots, pipeline, error contract, ports |
| [docs/concepts/tenancy-and-authorization.md](docs/concepts/tenancy-and-authorization.md) | OIDC/JWT, scopes and policies, claims, Jint rights checks |
| [docs/concepts/data-model.md](docs/concepts/data-model.md) | tables, pgvector `vector(768)`, indexes, cascade rules |
| [docs/concepts/payload-offloading.md](docs/concepts/payload-offloading.md) | Storage Service attachment contract |
| [docs/concepts/knowledge-pipeline.md](docs/concepts/knowledge-pipeline.md) | Docling ingestion → chunking → embedding → image vectors |
| [docs/concepts/rag-context-and-tools.md](docs/concepts/rag-context-and-tools.md) | system-prompt composition, top-k retrieval, tools and MCP |
| [docs/concepts/background-jobs.md](docs/concepts/background-jobs.md) | Quartz job keys, `JobDataMap` payloads, job mirroring |
| [docs/concepts/chat-application.md](docs/concepts/chat-application.md) | `Ballware.Llm.App` host and its differences vs. the Service |
| [docs/concepts/upstream-dependencies.md](docs/concepts/upstream-dependencies.md) | Meta/Storage/Docling/OpenAI-compatible endpoints, shared packages |
| [docs/api/README.md](docs/api/README.md) | the two Swagger documents, conventions, declared-vs-actual codes |
| [docs/api/generic-crud.md](docs/api/generic-crud.md) | the 11 generic operations × 5 resources |
| [docs/api/llm-api-resources.md](docs/api/llm-api-resources.md) | chat completions, simulate system prompt, job triggers |
| [docs/api/signalr-chat-hub.md](docs/api/signalr-chat-hub.md) | hub contract outside OpenAPI |
| [docs/api/schemas.md](docs/api/schemas.md) | field tables for all 15 component schemas |
| [docs/api/examples.md](docs/api/examples.md) | copy-pasteable sequences, incl. SSE transcript and job round-trip |
| [docs/api/metadata-templates.md](docs/api/metadata-templates.md) | synthetic tenant-metadata templates (non-normative) |
| [docs/openapi/README.md](docs/openapi/README.md) | snapshot generation, tool pin, leak warning, fallback |
| [docs/openapi/llm.json](docs/openapi/llm.json) | committed OpenAPI snapshot (60 paths, 15 schemas) |
| [docs/openapi/manifest.json](docs/openapi/manifest.json) | snapshot provenance |
