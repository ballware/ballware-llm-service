# Component schemas

All **17** component schemas of [../openapi/llm.json](../openapi/llm.json), with the class each one is
generated from. Entity schemas are the `Ballware.Llm.Data.Public` models mapped by Mapster
(`src/Ballware.Llm.Data.Ef/Mapping/StorageMappingProfile.cs`); "required" reflects the snapshot's
`required` arrays (Swashbuckle emits none unless the class has `[Required]` / C# `required` members).
JSON naming is PascalCase for entity payloads and lowercase for the OpenAI payloads — see
[README.md](README.md).

## Context

`src/Ballware.Llm.Data/Public/Context.cs`

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | public uuid (`uuid` column), **not** the database `id` bigint |
| `Identifier` | string | yes | context identifier referenced by chat (`Context.Identifier`); unique per tenant |
| `SystemPromptTemplate` | string | yes | SmartFormat template; placeholders `{GlobalContext}`, `{InitialPrompt}` |
| `WelcomeMessage` | string | yes | sent as the first `Response` event on hub connect |
| `NumCtx` | integer (int32) | yes | forwarded as `num_ctx` to the inference endpoint |
| `Model` | string | yes | per-context model override |

No `required` entries in the snapshot, although the database requires `identifier`
(`Persistables/Context.cs:31-34`).

## Origin

`src/Ballware.Llm.Data/Public/Origin.cs`

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | |
| `OwnerEntity` | string | yes | record the payload was attached to; the ingestion job writes the literal `"attachment"` (`OriginAddFromAttachmentJob.cs:53`) |
| `OwnerId` | string (uuid) | no | |
| `ReferingEntity` | string | yes | entity the ingestion was triggered for (`OriginAddFromAttachmentJob.cs:51`) |
| `ReferingId` | string (uuid) | no | |
| `Name` | string | yes | unique per `(tenant_id, owner_entity, owner_id, name)` |
| `Description` | string | yes | ingestion writes the literal `"None"` (`OriginAddFromAttachmentJob.cs:54`) |
| `GlobalContext` | boolean | no | `true` = injected into every system prompt |
| `GlobalRelevance` | integer (int32) | yes | ascending ordering of global origins (`ModelContextProvider.cs:27`) |
| `ContentType` | string | yes | ingestion result format, e.g. `text/markdown` |
| `Content` | string | yes | **not a database column** — Storage Service payload; materialized only by `byid`/`export` ([../concepts/payload-offloading.md](../concepts/payload-offloading.md)) |

## Embedding

`src/Ballware.Llm.Data/Public/Embedding.cs`

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | |
| `OriginId` | string (uuid) | no | unconstrained uuid (no foreign key) |
| `Model` | string | yes | embedding model used for the chunks |
| `Active` | boolean | no | only active embeddings are retrieved |
| `TopK` | integer (int32) | yes | per-embedding top-k; never set by the create job |
| `MaxTokensPerChunk` | integer (int32) | yes | effective value is written by the recreate job |
| `OverlapTokens` | integer (int32) | yes | same |

## Chunk

`src/Ballware.Llm.Data/Public/Chunk.cs`

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | |
| `EmbeddingId` | string (uuid) | no | parent embedding |
| `Text` | string | yes | max 102400 characters in the database (`Persistables/Chunk.cs:20-22`) |
| `Data` | array of number (float) | yes | the `vector(768)` payload |

`Chunk` has the generic editing routes, but the RAG pipeline reads it through
`FetchTopKSimilarChunksAsync`, which returns **text only** (`ChunkRepository.cs:23`) — the vector is not
part of retrieval output.

## Image

`src/Ballware.Llm.Data/Public/Image.cs`

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | |
| `OriginId` | string (uuid) | no | parent origin |
| `Name` | string | yes | max 512 characters; fallback `"image"` (`OriginAddFromAttachmentJob.cs:73`) |
| `Description` | string | yes | max 4096 characters |
| `ContentType` | string | yes | max 100 characters; fallback `"application/octet-stream"` (`OriginAddFromAttachmentJob.cs:75`) |
| `Text` | string | yes | max 16384 characters — the German-labelled description text that was embedded (`OriginAddFromAttachmentJob.cs:88-111`) |
| `Data` | array of number (float) | yes | the `vector(768)` payload |

The **binary** of an image is not in this schema — it lives in the Storage Service under owner entity
`"image"` ([../concepts/payload-offloading.md](../concepts/payload-offloading.md)).

## ChatSession

`src/Ballware.Llm.Data/Public/ChatSession.cs` — no `required` entries; every field is optional on input.

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | string (uuid) | no | public uuid (`chat_session.uuid`), **not** the database `id` bigint |
| `ContextIdentifier` | string | yes | the `context` the session belongs to; `new` binds it to the requesting principal's context set — a caller-supplied value is **ignored on `save`** |
| `Title` | string | yes | the only field `save` writes |
| `OwnerUserId` | string (uuid) | yes | **read-only**: mapped Persistable → Public only, never inbound (`StorageMappingProfile.cs`) |
| `CreateStamp` | string (date-time) | yes | |
| `LastChangeStamp` | string (date-time) | yes | |
| `Messages` | array of `ChatSessionMessage` | yes | filled on `byid` only; `null` on `all` and `query` |

## ChatSessionMessage

`src/Ballware.Llm.Data/Public/ChatSessionMessage.cs` — no `required` entries.

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Role` | string | yes | `user` / `assistant` / `system` as persisted by the chat UI |
| `Content` | string | yes | message text |
| `Sequence` | integer (int32) | no | 0-based order within the session (`MAX(sequence) + 1` on append) |
| `CreateStamp` | string (date-time) | yes | |

`ChatSession` has no `Payload`/`AttachmentId` and no export shape — see
[generic-crud.md](generic-crud.md).

## OriginAddFromAttachmentRequest

`src/Ballware.Llm.Api/Model/OriginAddFromAttachmentRequest.cs` — required: `AttachmentId`, `Name`,
`OwnerEntity`, `OwnerId` (all four `[Required]`).

| Field | JSON type | Notes |
| --- | --- | --- |
| `OwnerEntity` | string (`minLength: 1`) | |
| `OwnerId` | string (uuid) | |
| `Name` | string (`minLength: 1`) | |
| `AttachmentId` | string (uuid) | |

## OriginCreateEmbeddingRequest

`src/Ballware.Llm.Api/Model/OriginCreateEmbeddingRequest.cs` — required: `Model`.

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Ids` | array of string (uuid) | yes | origin ids; a null value causes a `NullReferenceException` → `500` |
| `Model` | string | snapshot marks it nullable but also lists it in `required` — it is a C# `required` member (`OriginCreateEmbeddingRequest.cs:7`), so omitting it fails model validation | |
| `Activate` | boolean | no | |
| `TopK` | integer (int32) | yes | **never forwarded** to the job |
| `TokensPerChunk` | integer (int32) | yes | forwarded as `tokensPerChunk` |
| `OverlapTokens` | integer (int32) | yes | forwarded as `overlapTokens` |

## RecreateEmbeddingRequest

`src/Ballware.Llm.Api/Model/EmbeddingRecreateRequest.cs` (file name differs from the class name).

| Field | JSON type | Nullable | Notes |
| --- | --- | --- | --- |
| `Ids` | array of string (uuid) | yes | **embedding** ids |

No `required` entries; an empty/absent `Ids` simply triggers no jobs.

## ContextSimulateSystemPromptRequest

`src/Ballware.Llm.Api/Model/ContextSimulateSystemPromptRequest.cs` — required: `ContextIdentifier`,
`InitialPrompt` (both C# `required`).

| Field | JSON type | Notes |
| --- | --- | --- |
| `ContextIdentifier` | string | |
| `InitialPrompt` | string | also used as the retrieval query vector source |

The **response** of this operation is not a component schema (see "Not in the snapshot" below).

## OpenAiChatCompletionRequest

`src/Ballware.Llm.Api/Model/OpenAiChatCompletionRequest.cs` — lowercase `JsonPropertyName`s.

| Field | JSON type | Notes |
| --- | --- | --- |
| `model` | string | interpreted as the **context identifier** |
| `messages` | array of `OpenAiChatCompletionMessage` | |
| `stream` | boolean | default `false` |

## OpenAiChatCompletionMessage

`src/Ballware.Llm.Api/Model/OpenAiChatCompletionMessage.cs`

| Field | JSON type | Notes |
| --- | --- | --- |
| `role` | string | `user` content is passed verbatim; any other role is flattened to `"<role>: <content>"` |
| `content` | string | blank content is dropped |

## OpenAiChatCompletionResponse

`src/Ballware.Llm.Api/Model/OpenAiChatCompletionResponse.cs`

| Field | JSON type | Notes |
| --- | --- | --- |
| `id` | string | `chatcmpl-{Guid:N}` |
| `object` | string | default `chat.completion` |
| `created` | integer (int64) | Unix seconds |
| `model` | string | echoed context identifier |
| `choices` | array of `OpenAiChatCompletionChoice` | always exactly one, `index: 0` |

## OpenAiChatCompletionChoice

| Field | JSON type | Notes |
| --- | --- | --- |
| `index` | integer (int32) | |
| `message` | `OpenAiChatCompletionMessage` | `role: "assistant"` |
| `finish_reason` | string | always `"stop"` (`ChatEndpoint.cs:138`) |

## ExportUrlResult

From `ballware-shared-dotnet` (`Ballware.Shared.Api/Public/ExportUrlResult.cs`), produced by
`exporturl`.

| Field | JSON type | Notes |
| --- | --- | --- |
| `TenantId` | string (uuid) | |
| `Id` | string (uuid) | export record id, used in `download/{tenantId}/{id}` |

## StringStringValuesKeyValuePair

Generated from the `IFormCollection` accepted by `exporturl`
(`TenantantableEditingEndpoint.cs:116`): the form body is described as an array of these pairs, which is
how Swashbuckle renders a form collection — in practice you send flat form fields
(`param1=value1&param2=value2`).

| Field | JSON type | Notes |
| --- | --- | --- |
| `Key` | string | |
| `Value` | array of string | one entry per repeated field |

## Not in the snapshot

Declared responses without a typed body — their shapes are taken from source and are documented in
[llm-api-resources.md](llm-api-resources.md):

| Operation | Declared response | Actual body (source) |
| --- | --- | --- |
| `POST /ai/context/simulatesystemprompt` | `200`, no content | `ContextSimulateSystemPromptResponse { SystemPrompt, EstimatedTokenCount }` (`ContextEndpoint.cs:50-54`) |
| `POST /ai/v1/chat/completions` with `stream: true` | `200`, no content | `text/event-stream`; chunk objects come from `OpenAiChatCompletionStreamResponse` (`{ id, object: "chat.completion.chunk", created, model, choices: [{ index, delta: { role?, content? }, finish_reason? }] }`), which is **not** a component schema |
| `POST /ai/origin/addfromattachment`, `/ai/origin/createembedding`, `/ai/embedding/recreate` | `201` (actual `202`/`202`/`201`), no content | empty body |
| `POST /ai/<resource>/save`, `/savebatch` | `200`, no content | empty body |
| `GET /ai/<resource>/export` | `application/json` **string** schema | raw export body with the export definition's media type |
