# Upstream dependencies

What the service calls, in which code path, and with which credentials.

## ballware Meta Service

Metadata, exports and job records. Client: NuGet `ballware.meta.service.client` **1.3.2**
(`src/Ballware.Llm.Hosting/Ballware.Llm.Hosting.csproj:15`), typed `HttpClient<MetaServiceClient>` with
`BaseAddress = MetaClient.ServiceUrl` (`src/Ballware.Llm.Hosting/LlmHostingExtensions.cs:202-217`).

| Call | Used for | Adapter line |
| --- | --- | --- |
| `TenantServiceMetadataAsync(tenantId)` | tenant `RightsCheckScript` for the tenant-level rights stage | `Adapter/MetaServiceMetadataAdapter.cs:20-25` |
| `EntityServiceMetadataForTenantByIdentifierAsync(tenantId, entity)` | entity metadata whose `CustomScripts.ExtendedRightsCheck` becomes the entity rights script | `:27-32` |
| `ExportCreateForTenantBehalfOfUserAsync(tenantId, userId, payload)` | `exporturl` (`Application`, `Entity`, `Query`, `ExpirationStamp`, `MediaType`) | `:34-44` |
| `ExportFetchForTenantByIdAsync(tenantId, exportId)` | export record lookup for the download | `:46-59` |
| `JobCreateForTenantBehalfOfUserAsync(tenantId, userId, { Scheduler, Identifier, Options })` | import job record; `null` result → `InvalidOperationException("Failed to create job for tenant <id>.")` | `:61-75` |
| `JobUpdateForTenantBehalfOfUserAsync(tenantId, userId, { Id, State, Result })` | import job state transitions | `:78-86` |

Authorization: **client credentials** — `MetaClient.CredentialIdentifier` selects an entry from
`ServiceCredentials.Credentials[]` (`Identifier`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scopes`),
and a Duende handler attaches a token to the typed client
(`LlmHostingExtensions.cs:147-161, 214-217`). The tenant and user are arguments ("on behalf of").

Consequence for availability: every generic editing request performs at least two Meta Service calls
(tenant + entity metadata). If the Meta Service is unreachable or the tenant lacks `ai.<entity>`
metadata, the request fails with `500` or `404` — see
[tenancy-and-authorization.md](tenancy-and-authorization.md).

## ballware Storage Service

Attachments and temporary files. Client: `ballware.storage.service.client` **9.3.1**
(`Ballware.Llm.Hosting.csproj:16`), typed `HttpClient<StorageServiceClient>` with
`BaseAddress = StorageClient.ServiceUrl` (`LlmHostingExtensions.cs:185-200`), same
client-credentials mechanism via `StorageClient.CredentialIdentifier`.

| Call | Used for | Adapter line |
| --- | --- | --- |
| `AttachmentDownloadForTenantEntityAndOwnerByIdAsync` | read the source document for `addfromattachment`; content type read from the response header | `Adapter/StorageServiceFileStorageAdapter.cs:33-43` |
| `AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync` | image binary upload (owner entity `"image"`) | `:45-50` |
| `AttachmentUploadForTenantEntityAndOwnerBehalfOfUser` (sync) | origin text upload (owner entity `"origin"`) | `Extensions/OriginPayloadRepositoryHook.cs:34-35` |
| `AttachmentDownloadForTenantEntityAndOwnerByFilename` (sync) | origin text read-back | `:12` |
| `AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser` (sync) | origin/image attachment cleanup | `:42`, `Extensions/ImagePayloadRepositoryHook.cs:24-29` |
| `TemporaryUploadForTenantAndIdBehalfOfUserAsync` / `TemporaryDownloadForTenantByIdAsync` / `TemporaryDropForTenantAndIdBehalfOfUserAsync` | `import` staging and cleanup | `StorageServiceFileStorageAdapter.cs:15-31` |

Contract details (file naming, encoding) in [payload-offloading.md](payload-offloading.md).

## Docling Serve

Document conversion. Base address `Docling.Url`, per-request timeout `Docling.RequestTimeoutSeconds`
(`src/Ballware.Llm.Service/Startup.cs:133-137`). Only three operations are used:

```text
POST v1/convert/file/async      multipart: files (+ filename "input"), output_format, image_export_mode
GET  v1/status/poll/{task_id}   1 s polling loop, `wait` omitted
GET  v1/result/{task_id}        raw string body
```

Details and deviations in [knowledge-pipeline.md](knowledge-pipeline.md).

The upstream specification is committed at
[`src/Ballware.Llm.Docling.Client/openapi.json`](../../src/Ballware.Llm.Docling.Client/openapi.json):
`info.title = "Docling Serve"`, `info.version = "1.14.2"`, OpenAPI `3.1.0`, **no `servers`**, 21 paths
(`convert/source|file`, `convert/*/async`, `chunk/hybrid|hierarchical/*`, `status/poll/{task_id}`,
`result/{task_id}`, `health`, `version`, `openapi-3.0.json`, `clear/converters`, `clear/results`,
`memory/stats`, `memory/counts`). It declares an `APIKeyAuth` scheme (`X-Api-Key`), which this repo never
sets. **Reference it, do not copy it into `docs/openapi/`** — this folder documents only this service's
own API.

The generated client `DoclingApiClient.cs` (NSwag 14.7.1, 15,302 lines) is part of the build; regenerate
it with `nswag run nswag.json` inside `src/Ballware.Llm.Docling.Client` (CI installs `Nswag.ConsoleCore`,
though the generation step is disabled — `.github/workflows/publish-packages.yml:33, 60-64`).

## OpenAI-compatible endpoints

Two separate endpoints, both configured per host:

| Section | Keys | Used by |
| --- | --- | --- |
| `Inference` | `Endpoint` (required), `ApiKey` (required), `Model` (optional) | `AiInferenceEngine` → `ChatClient` → `POST <endpoint>/v1/chat/completions` |
| `Embedding` | `Endpoint` (required), `ApiKey` (required), `Model` (optional) | `OpenAiEmbeddingEngine` → `EmbeddingClient` → `POST <endpoint>/v1/embeddings` |

`/v1` is appended automatically when the configured path is empty or does not already end with it
(`AiInferenceEngine.cs:24-45`, `OpenAiEmbeddingEngine.cs:22-43`), so plain hosts, subpath gateways and
`/v1`-terminated URLs all work. Implemented against `OpenAI` **2.10.0** + `Microsoft.Extensions.AI`
**10.5.0** / `Microsoft.Extensions.AI.OpenAI` **10.5.2**
(`src/Ballware.Llm.Inference.OpenAi/Ballware.Llm.Inference.OpenAi.csproj:15-21`), and verified in practice
against Ollama (the log strings even say "Ollama API"). No retry, timeout, or auth-scheme configuration
exists for these two clients — the API key is sent as `Authorization: Bearer <ApiKey>`.

Because `vector(768)` is fixed in the schema, the embedding endpoint must return 768-dimensional vectors
(see [data-model.md](data-model.md)).

## MCP servers (optional)

`Mcp.Agents[]` (`Namespace`, `ServiceUrl`, `UserTokenDelegation`, `CredentialIdentifier`) each get a named
`HttpClient` and are enumerated with `ModelContextProtocol.Core` **1.1.0** over
`HttpClientTransport` (streamable HTTP). Details in
[rag-context-and-tools.md](rag-context-and-tools.md). No `Mcp` section is shipped in either host's tracked
config, so MCP is off by default.

## OIDC authority

`Authorization.Authority` / `Audience` / optional `Issuer` drive JWT validation
(`src/Ballware.Llm.Service/Startup.cs:78-84`). Client-credentials flows use
`ServiceCredentials.Credentials[].TokenEndpoint`. The Swagger environment replaces all of it with
`https://localhost` placeholders — see [../openapi/README.md](../openapi/README.md).

## PostgreSQL + pgvector

Connection string key **`LlmStorageConnection`** (`LlmHostingExtensions.cs:41, 225`), provider switch
`Llm.Provider = postgres` (`:232`). Packages: `Npgsql` 10.0.1, `Npgsql.EntityFrameworkCore.PostgreSQL`
10.0.0, `Pgvector.EntityFrameworkCore` 0.3.0
(`src/Ballware.Llm.Data.Ef.Postgres/Ballware.Llm.Data.Ef.Postgres.csproj:13-23`). The `vector` extension
and the Quartz job-store tables must be provisioned by the deployment — see
[data-model.md](data-model.md) and [background-jobs.md](background-jobs.md).

## Shared platform packages

| Package | Version | Provides |
| --- | --- | --- |
| `ballware.shared.api.endpoints` | 1.1.0 | the 11 generic editing operations, `ExportUrlResult`, bindings, `IFileStorageProvider`, `IJobMetadataProvider` |
| `ballware.shared.api.jobs` | 1.1.0 | `TenantableImportJob` |
| `ballware.shared.authorization` | 1.1.0 | `IPrincipalUtils`, `ITenantRightsChecker`, `IEntityRightsChecker` contracts |
| `Ballware.Shared.Authorization.Jint` | 1.1.0 | the Jint-based checkers actually registered |
| `Ballware.Shared.Data` / `Ballware.Shared.Data.Ef` | 1.1.0 | `IEditable`/`IEntity`/`ITenantable`/`IAuditable`, `ITenantableRepository`, `ITenantableRepositoryHook`, `TenantableBaseRepository` |
| `Duende.AccessTokenManagement` | 4.1.1 | client-credentials token management |
| `Mapster.DependencyInjection` | 1.0.1 | `IMapper` + the mapping profiles |
| `Quartz` / `Quartz.AspNetCore` / `Quartz.Serialization.Json` | 3.8.1 | scheduler, hosted server, JSON job data |
| `Swashbuckle.AspNetCore` / `.Annotations` | 10.1.0 | Swagger documents (and the pinned CLI version) |
| `SmartFormat.NET` | 3.6.1 | system-prompt templating |
| `Microsoft.SemanticKernel` | 1.74.0 | `TextChunker` used by `MarkdownChunker` |
| `MimeTypeMapOfficial` | 1.0.17 | content-type → file extension for payload names |

## Related pages

- [../openapi/README.md](../openapi/README.md) — what is *not* reached during snapshot generation
- [architecture.md](architecture.md) — where each client is registered
