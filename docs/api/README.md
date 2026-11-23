# API surface and conventions

The service serves two Swagger documents per deployment, each with its own Swagger UI page and raw
JSON (`src/Ballware.Llm.Service/Startup.cs:259-275`):

| Document | Title | Paths | Schemas | Swagger JSON | Committed snapshot |
| --- | --- | --- | --- | --- | --- |
| `llm` | ballware LLM user API | 60 | 15 | `/swagger/llm/swagger.json` | [../openapi/llm.json](../openapi/llm.json) |
| `service` | ballware LLM service API | **0** | 0 | `/swagger/service/swagger.json` | — **not exported** |

Titles and version `v1` are defined in `Startup.cs:170-199`; the UI wiring is `Startup.cs:261-275`
(`OAuthClientId`/`ClientSecret`/`Scopes`, `OAuthUsePkce()` from the `Swagger` section).

## The `service` document is a reserved placeholder

The `service` document is declared (`Startup.cs:178-182`) and the `serviceApi` authorization policy is
defined (`Startup.cs:113-118`, scope name from `Authorization:RequiredServiceScope`, default
`serviceApi` — `src/Ballware.Llm.Service/Configuration/AuthorizationOptions.cs:27`), but **no endpoint
is mounted into that group**: every `MapTenantableEditingApi` and `MapXUserApi` call passes
`apiGroup = "llm"` (`Startup.cs:245-254`, `Ballware.Llm.Api/Endpoints/*.cs`). At runtime
`/swagger/service/swagger.json` therefore resolves to a document with an empty `paths` object. Treat
"LLM service API" as reserved capacity, not as an API surface — do not create a client, sidebar
section, or aggregation entry from it.

## Grouping, tags, operation ids

- Each endpoint is assigned the group `llm` (`.WithGroupName("llm")`) and a resource tag
  (`.WithTags(...)`). Snapshot tags: `Chat`, `Chunk`, `Context`, `Embedding`, `Image`, `Origin`.
- Operation ids follow `<OperationPrefix><Name>`: `OriginAll`, `ContextById`, `ChunkRemove`,
  `ImageDownload`, plus the specific ones `OpenAiChatCompletions`, `ContextSimulateSystemPrompt`,
  `OriginAddFromAttachment`, `OriginCreateEmbedding`, `EmbeddingRecreate`.
- Generic operations come from
  `ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:22-132`.

## Path inventory

| Group | Count | Where |
| --- | --- | --- |
| Chat | 1 | `POST /ai/v1/chat/completions` (`Startup.cs:245`) |
| Context | 12 | 1 specific (`simulatesystemprompt`) + 11 generic (`Startup.cs:246, 250`) |
| Embedding | 12 | 1 specific (`recreate`) + 11 generic (`Startup.cs:248, 252`) |
| Origin | 13 | 2 specific (`addfromattachment`, `createembedding`) + 11 generic (`Startup.cs:247, 251`) |
| Chunk | 11 | generic only (`Startup.cs:253`) |
| Image | 11 | generic only (`Startup.cs:254`) |
| **Total** | **60** | — |

The SignalR hub `/ai/hubs/chat` (`Startup.cs:244`) is **not** part of any OpenAPI document — see
[signalr-chat-hub.md](signalr-chat-hub.md).

## Payload conventions

- Request and response JSON is **PascalCase** for all entity payloads and LLM-specific DTOs
  (`PropertyNamingPolicy = null` for both MVC and minimal-API JSON options, `Startup.cs:121-129`):
  `{ "Id": …, "Identifier": …, "SystemPromptTemplate": … }`.
- The OpenAI chat payloads are the deliberate exception: `OpenAiChatCompletionRequest`/`Response`/
  `StreamResponse` set explicit `JsonPropertyName` values, so they stay lowercase and snake_case
  (`model`, `messages`, `stream`, `choices[].finish_reason`) as required by the OpenAI wire format
  (`src/Ballware.Llm.Api/Model/OpenAiChatCompletionResponse.cs`, `…/OpenAiChatCompletionStreamResponse.cs`).
- Send `Content-Type: application/json` for JSON bodies. `import` expects
  `multipart/form-data` (field `files`), `exporturl` expects `application/x-www-form-urlencoded`
  (`TenantantableEditingEndpoint.cs:116`).
- UUIDs are plain GUID strings; `Data` (vector) is a JSON array of numbers.
- `Origin.Content` is accepted on write and materialized on `byid`, but is **not** a database column —
  see [../concepts/payload-offloading.md](../concepts/payload-offloading.md).

## Security scheme

The snapshot declares one security scheme:

```json
"oidc": { "type": "openIdConnect", "openIdConnectUrl": "https://localhost/.well-known/openid-configuration" }
```

with a **document-level** requirement `[ { "oidc": [ "openid", "mlApi" ] } ]`
(`Startup.cs:186-198`; scopes from `Swagger:RequiredScopes`, default `openid mlApi` per
`src/Ballware.Llm.Service/Configuration/SwaggerOptions.cs:7`). The `https://localhost` host is the
Swagger-environment placeholder, not a real IdP — see [../openapi/README.md](../openapi/README.md).

Two snapshot-level quirks to know when generating clients:

- Per-operation `security` blocks are **not** emitted; only the global requirement exists.
- The 5 `download/{tenantId}/{id}` routes are mapped `.AllowAnonymous()`
  (`TenantantableEditingEndpoint.cs:126`) and require no token at runtime, but the snapshot's global
  requirement makes them look authenticated.

## The `identifier` query parameter

Generic editing operations do not use fixed SQL semantics — they resolve a **query definition by
identifier** from the entity's tenant metadata in the Meta Service. Conventions
(`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantableEndpointHandlerFactory.cs`):

| Operation family | `identifier` parameter | Notes |
| --- | --- | --- |
| `all`, `query`, `new`, `byid`, `save`, `savebatch`, `import`, `export`, `exporturl` | required query parameter | value must exist in the entity's metadata payloads; `primary` is the platform default (`TenantableEndpointHandlerFactory.cs:17`) |
| `remove/{id}` | absent | uses `primary` internally (`TenantableEndpointHandlerFactory.cs:159`) |
| `download/{tenantId}/{id}` | absent | file download of a prior `exporturl` result |

For `save`/`savebatch` a non-`primary` identifier changes the required right
(`ai.<entity>.<identifier>` instead of `ai.<entity>.edit`); for `export`/`exporturl` the identifier
**is** the right. Details in [generic-crud.md](generic-crud.md).

Three entity repositories additionally define a `ListQuery` for a scope identifier that requires a
filter parameter: `embedding` and `chunk`/`image` are reachable through the generic `query` with
`identifier=origin` / `identifier=embedding` plus the `origin` / `embedding` parameter
(`src/Ballware.Llm.Data.Ef/Repository/EmbeddingBaseRepository.cs:16-29`,
`…/ChunkBaseRepository.cs:10-23`, `…/ImageBaseRepository.cs:11-24`); a missing parameter raises
`ArgumentException`, which surfaces as `500` `application/problem+json`.

## Declared vs. actual status codes

Three job-trigger endpoints declare a status that differs from what the handler returns:

| Operation | Declared in `llm.json` | Returned by the handler |
| --- | --- | --- |
| `POST /ai/origin/addfromattachment` | `201` (`OriginEndpoint.cs:27`) | **`202`** — `Results.Accepted()` (`OriginEndpoint.cs:69`) |
| `POST /ai/origin/createembedding` | `201` (`OriginEndpoint.cs:37`) | **`202`** — `Results.Accepted()` (`OriginEndpoint.cs:105`) |
| `POST /ai/embedding/recreate` | `201` (`EmbeddingEndpoint.cs:27`) | `201` — `Results.Created()` (`EmbeddingEndpoint.cs:58`) |

[llm-api-resources.md](llm-api-resources.md) and [examples.md](examples.md) document the **actual**
codes and note the drift.

## Error responses

- `500`-class failures return `application/problem+json` with
  `{ "type": "https://httpstatuses.com/500", "title": "An unexpected error occurred.", "status": 500, "instance": "<path>" }`
  (`Startup.cs:211-236`; `detail` is populated only in Development).
- `401` is produced by the scope policy or a denied right (`EditingEndpointBuilder.cs:111-114`) with an
  empty body.
- `404` bodies from the generic handlers are **plain text**: `Tenant <id> not found` or
  `Entity <name> not found for tenant <id>` (`EditingEndpointBuilder.cs:72-86`).
- `400` on the chat endpoint is a **plain-text** reason string (see
  [llm-api-resources.md](llm-api-resources.md)).

## Obtaining tokens

- Swagger UI (`/swagger`) uses the OIDC button with `Swagger.ClientId` and `Swagger.RequiredScopes`
  (authorization code + PKCE, `Startup.cs:270-273`).
- The documented operations all need a **user token carrying the `mlApi` scope** and a `tenant` claim.
  Service-to-service client-credentials tokens carry `serviceApi` (`AuthorizationOptions.cs:27`) and
  are therefore not accepted by any published endpoint today.
