# Generic CRUD operations

Every LLM resource (`context`, `origin`, `embedding`, `chunk`, `image`, `session`) is mounted with the
same 11 generic editing operations from
`ballware-shared-dotnet/src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs:22-132`
(handlers in `TenantableEndpointHandlerFactory.cs`) — 11 paths per resource, 66 paths in
[../openapi/llm.json](../openapi/llm.json):

```text
/ai/context    /ai/origin    /ai/embedding    /ai/chunk    /ai/image    /ai/session
```

The mounts are `Startup.cs:250-255`; each passes `application="ai"`, `entity="<resource>"`, tag and
operation prefix `<Resource>` (PascalCase), policy `mlApi`, group `llm`.

## Operation overview

`<resource>` ∈ {`context`, `origin`, `embedding`, `chunk`, `image`, `session`}:

| # | Method | Path | Parameters | Request body | Success response | Right candidate |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | GET | `/ai/<resource>/all` | `identifier` (query, required) | – | `200`, array of `<resource>` schema | `<resource>.view` |
| 2 | GET | `/ai/<resource>/query` | `identifier` (query, required) + free query params | – | `200`, array of `<resource>` schema | `<resource>.view` |
| 3 | GET | `/ai/<resource>/new` | `identifier` (query, required) | – | `200`, `<resource>` schema | `<resource>.add` |
| 4 | GET | `/ai/<resource>/byid` | `identifier`, `id` (both query, required; `id` is uuid) | – | `200`, `<resource>` schema; `404` if not found | `<resource>.view` |
| 5 | POST | `/ai/<resource>/save` | `identifier` (query, required) | `<resource>` JSON | `200` empty | `<resource>.edit` or `<resource>.<identifier>` |
| 6 | POST | `/ai/<resource>/savebatch` | `identifier` (query, required) | array of `<resource>` JSON | `200` empty | `<resource>.edit` or `<resource>.<identifier>` |
| 7 | DELETE | `/ai/<resource>/remove/{id}` | `id` (path, uuid) | – | `200` (declared as `<resource>` schema); `400` if a remove precondition fails | `<resource>.delete` |
| 8 | POST | `/ai/<resource>/import` | `identifier` (query, required) | `multipart/form-data`, field `files` (file array) | `201` empty; schedules a job | meta scope only (job checks `<resource>.<identifier>`) |
| 9 | GET | `/ai/<resource>/export` | `identifier` (query, required) + query params | – | `200`, raw export body | `<resource>.<identifier>` |
| 10 | POST | `/ai/<resource>/exporturl` | `identifier` (query, required) | `application/x-www-form-urlencoded` fields | `200`, `ExportUrlResult` | `<resource>.<identifier>` |
| 11 | GET | `/ai/<resource>/download/{tenantId}/{id}` | `tenantId`, `id` (path, uuid) | – | `200`, file content | **anonymous** |

Right candidates are written as `<resource>.<right>`; the full candidate evaluated in the scripts is
`ai.<resource>.<right>` because the mount sets `application="ai"` (`Startup.cs:250-255`). The candidate
grammar and the two script stages are described in
[../concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md). Source of the
per-operation candidates: `TenantableEndpointHandlerFactory.cs:19-22` (`view`/`add`/`edit`/`delete`
constants), `:66`, `:79`, `:91`, `:110` (view/add), `:122-123`, `:140-141` (save), `:168-169`
(remove), `:238`, `:265` (export), `:192-219` (import: no right check at the endpoint).

## Query and by-id semantics

- `identifier` selects which embedded query definition runs; `primary` is the platform default
  (`TenantableEndpointHandlerFactory.cs:17`).
- Extra query parameters on `query` are passed to the query definition as parameters. A parameter
  repeated multiple times arrives merged as `|value1|value2|`
  (`TenantableEndpointHandlerFactory.cs:312-329`):

  ```text
  GET /ai/origin/query?identifier=primary&name=alpha&name=beta   →   name = "|alpha|beta|"
  ```

- `byid` takes `identifier` **and** `id` as query parameters — `identifier` selects the `ByIdQuery`
  entry, `id` is the item's `uuid`:

  ```text
  GET /ai/origin/byid?identifier=primary&id=<guid>
  ```

- LLM-specific list scopes exist on three entities and require a filter parameter:

  | Resource | `identifier` | Required parameter | Source |
  | --- | --- | --- | --- |
  | `embedding` | `origin` | `origin` = origin uuid | `src/Ballware.Llm.Data.Ef/Repository/EmbeddingBaseRepository.cs:16-29` |
  | `chunk` | `embedding` | `embedding` = embedding uuid | `src/Ballware.Llm.Data.Ef/Repository/ChunkBaseRepository.cs:10-23` |
  | `image` | `origin` | `origin` = origin uuid | `src/Ballware.Llm.Data.Ef/Repository/ImageBaseRepository.cs:11-24` |

  Example: `GET /ai/chunk/query?identifier=embedding&embedding=<embedding-uuid>`. A missing filter
  parameter raises `ArgumentException` → `500` `application/problem+json`.

- `session` accepts an optional `context` parameter on `all`/`query` to restrict the list to one chat
  context (`ChatSessionBaseRepository.ListQuery`). Without it, the list spans all contexts of the actor.

## List vs. detail: `Origin.Content`

`Origin.Content` is part of the `Origin` schema, but the payload lives in the Storage Service. Only the
**by-id** path materializes it (`TenantableBaseRepository.cs:132` invokes `ExtendByIdAsync`, which
`OriginPayloadRepositoryHook.cs:8-23` implements); `all` and `query` map rows directly
(`TenantableBaseRepository.cs:106-116`) and return `Content` empty. Read
[../concepts/payload-offloading.md](../concepts/payload-offloading.md).

The same pattern applies to `ChatSession.Messages`: only `byid` fills it (the latest
`Chat:MaxHistoryMessages` messages, ascending, hard-capped at 500); `all` and `query` return
`Messages = null`. **`null` means "not loaded", not "empty conversation"** — read the history with
`byid`.

## Save

`save` posts a single document; `savebatch` posts an array and is checked item by item
(`TenantableEndpointHandlerFactory.cs:133-152`). Responses are empty `200` — re-read via `byid` to get
the stored document.

```bash
curl -s -X POST "http://localhost:5006/ai/context/save?identifier=primary" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "Id": "<guid>", "Identifier": "support", "SystemPromptTemplate": "{GlobalContext}\n\nQuestion: {InitialPrompt}", "WelcomeMessage": "Hi", "NumCtx": 8192, "Model": "qwen3" }'
```

Saving an `Origin` with a non-empty `Content` pushes the payload to the Storage Service
(`OriginPayloadRepositoryHook.cs:25-38`, only for `identifier=primary` and a resolvable user).

A non-`primary` identifier (e.g. `?identifier=<customSaveIdentifier>`) requires the right
`ai.<entity>.<customSaveIdentifier>` instead of `ai.<entity>.edit`
(`TenantableEndpointHandlerFactory.cs:120-124`) and runs the matching entry of the entity's
`SaveStatement` payloads.

## Remove

`remove/{id}` loads the item with the `primary` by-id query, checks `ai.<entity>.delete`, then runs the
entity's remove precondition script and remove statement
(`TenantableEndpointHandlerFactory.cs:154-184`). Failed preconditions → `400` with the joined messages
as body.

Deleting an **origin** cascades at the application level (no database foreign keys): its embeddings —
and each embedding's chunks — plus its images are removed first
(`src/Ballware.Llm.Data.Ef/Repository/OriginBaseRepository.cs:12-42`,
`…/EmbeddingBaseRepository.cs:31-48`). Deleting an embedding removes its chunks. Image attachments and
origin payloads are dropped from the Storage Service by the repository hooks
(`src/Ballware.Llm.Hosting/Extensions/ImagePayloadRepositoryHook.cs:17-30`,
`…/OriginPayloadRepositoryHook.cs:40-43`).

## Export → exporturl → download

`export` returns the export payload inline; `exporturl` persists it as an `Export` record in the Meta
Service plus a temporary file and returns an anonymous download reference valid for one day
(`TenantableEndpointHandlerFactory.cs:249-288`):

```text
GET  /ai/<resource>/export?identifier=<exportIdentifier>&<query params>   raw body, media type from the export definition
POST /ai/<resource>/exporturl?identifier=<exportIdentifier>               form fields → query params → ExportUrlResult {TenantId, Id}
     form body: param1=value1&param2=value2                               Export record expires = now + 1 day (L275)
GET  /ai/<resource>/download/<tenantId>/<id>                              anonymous; file named <Query>_<timestamp><ext> (L308)
                                                                        404 when Export not found or ExpirationStamp passed (L296-299)
```

Payload/response details:

- `export` responses are declared `application/json` with a **string** schema in the snapshot
  (`Produces<string>()`, `TenantantableEditingEndpoint.cs:105`); at runtime the content type is the
  export definition's media type and the body is the raw export data.
- `exporturl` accepts form fields — every field becomes a query parameter of the export query
  definition (`TenantableEndpointHandlerFactory.cs:254-261`).
- `ExportUrlResult` = `{ "TenantId": "<guid>", "Id": "<guid>" }` (snapshot schema `ExportUrlResult`).
- `export` runs the by-id/extend path, so an exported `Origin` **does** include its offloaded
  `Content` (`TenantableBaseRepository.cs:258`).

## Import

`import` uploads one or more files (form field `files`) and returns `201 Created` immediately; the
actual import runs as a Quartz job. The request itself only needs the `mlApi` scope; the job checks the
right per item (`TenantableEndpointHandlerFactory.cs:188-220`).

```bash
curl -s -X POST "http://localhost:5006/ai/embedding/import?identifier=primary" \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@embeddings.json"
```

Only `origin` and `embedding` have import jobs registered (`origin.import`, `embedding.import` —
`src/Ballware.Llm.Jobs/ServiceCollectionExtensions.cs:21-22`); `chunk`, `image` and `context` accept
the route (it is part of the generic mount) but no matching Quartz `JobKey` exists, so the trigger
fails inside the scheduler. See [../concepts/background-jobs.md](../concepts/background-jobs.md).

> Quirk worth knowing: the shared import job builds its right candidate with the application literal
> `"meta"` rather than the mount's `"ai"` —
> `TenantRightsChecker.HasRightAsync(tenant, "meta", jobKey.Group, claims, identifier)`
> (`ballware-shared-dotnet/src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs:67`), i.e.
> `meta.origin.primary` / `meta.embedding.primary`. Tenant metadata that only defines
> `ai.origin.primary` will let the import job deny every item.

## `session` specifics

`/ai/session` reuses the generic mount but the repository narrows it down
(`src/Ballware.Llm.Data.Ef/Repository/ChatSessionBaseRepository.cs`):

| Operation | Behaviour for `session` |
| --- | --- |
| `all` / `query` | owner-scoped (`user_id` = acting user) unless the actor holds `ai.session.admin`; optional `context=<identifier>` filter; ordered by `last_change_stamp` descending |
| `byid` | owner-scoped; foreign session ⇒ `404`; **includes `Messages`** |
| `new` | returns a template with `OwnerUserId` stamped to the acting user; it does **not** persist a row — sessions are created lazily by the chat UI with the first user turn |
| `save` / `savebatch` | **rename only.** `Title` is the only writable member; an incoming `ContextIdentifier`, `OwnerUserId` or `Messages` is dropped, and `UserId` is never re-assigned. Unknown or foreign id ⇒ `404` (`ChatSessionNotVisibleException`) |
| `remove/{id}` | owner-scoped; deletes the session **and its `chat_message` rows**; foreign id ⇒ `404` |
| `import` / `export` / `exporturl` / `download` | mounted by the shared helper but **unsupported**: no Quartz `JobKey.Create("import", "session")` exists and no export definition is configured. Deny them in tenant metadata (`ai.session.import`, export identifiers) |

The DTO has no `UserId` member at all — ownership can never be written through the API
(`src/Ballware.Llm.Data/Public/ChatSession.cs`). Session rows are also deleted when their `context`
is removed.

## Status codes

`401` (missing scope / denied right), `404` (tenant, entity metadata or item not found — including a
chat session owned by another user), `400` (remove preconditions), `500` as `application/problem+json`
— see [../concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md).
