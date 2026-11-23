# LLM API resources

Five operations are mounted outside the generic editing family (`Startup.cs:245-248`). All five are in
the `llm` group of [../openapi/llm.json](../openapi/llm.json). The 55 generic operations are described
once in [generic-crud.md](generic-crud.md).

## `POST /ai/v1/chat/completions`

OpenAI-compatible chat completion (`ChatEndpoint.cs:26-40`, mounted by
`MapOpenAiChatCompletionsEndpoint("/ai/v1")` at `Startup.cs:245`).

**Authentication:** `.RequireAuthorization()` with **no policy name** (`ChatEndpoint.cs:29`) — the
default policy, i.e. any valid bearer token. Unlike every other documented operation it does **not**
require the `mlApi` scope.

**Request body** (`OpenAiChatCompletionRequest`):

| Field | Type | Meaning |
| --- | --- | --- |
| `model` | string | **the context identifier** — `body.Model` is used as `Context.Identifier` (`ChatEndpoint.cs:52-55, 73`), not as a model name |
| `messages` | array of `{ role, content }` | flattened into a single prompt (see below) |
| `stream` | bool | `true` → SSE (`text/event-stream`), default `false` |

Only these three fields are read. `temperature`, `top_p`, `max_tokens`, `tools`, `stop`, `n` are absent
from the model and silently ignored.

**Message flattening** (`ChatEndpoint.cs:112-119`): messages with blank `content` are dropped; the
remaining ones are joined with a newline. A `user` role is passed verbatim, every other role becomes
`"<role>: <content>"` — so a client-supplied multi-turn history arrives as one user prompt and a
`system` message becomes the literal text `system: …`.

**Validation errors** (`400`, plain-text body — `Results.BadRequest(string)`):

| Body text | Cause | Source |
| --- | --- | --- |
| `The requested model must contain the context identifier.` | `model` missing/whitespace | `ChatEndpoint.cs:52-55` |
| `At least one message is required.` | `messages` empty | `ChatEndpoint.cs:57-60` |
| `At least one message with content is required.` | all messages blank after filtering | `ChatEndpoint.cs:64-67` |

**Behavior:** a new `IChatSession` is created per HTTP request (`ChatEndpoint.cs:84-89`) — the endpoint
is **stateless**; the system prompt and retrieved knowledge are computed once per request from the
flattened prompt. Tenant, user, claims and the access token come from the bearer token
(`ChatEndpoint.cs:69-72`) and are used for tool resolution and MCP delegation only.

**Response (`stream: false`)** — `200`, `OpenAiChatCompletionResponse` (`ChatEndpoint.cs:121-142`):

```json
{
  "id": "chatcmpl-<32 hex>",
  "object": "chat.completion",
  "created": 1764000000,
  "model": "support",
  "choices": [
    { "index": 0, "message": { "role": "assistant", "content": "…" }, "finish_reason": "stop" }
  ]
}
```

`object` is the schema default (`OpenAiChatCompletionResponse.cs`), `id` is
`chatcmpl-{Guid:N}`, `created` is the Unix timestamp in seconds.

**Response (`stream: true`)** — `200`, `text/event-stream`, each event is `data: <json>\n\n`
(`ChatEndpoint.cs:144-235`): a role chunk, one content chunk per delta, a `finish_reason: "stop"`
chunk, then `data: [DONE]`. Full transcript in [examples.md](examples.md).

**Status codes:** `200`, `400`, `401` (`ChatEndpoint.cs:31-33`). Errors from the inference layer are not
mapped to 4xx/5xx here — an upstream failure escapes the streaming write and surfaces as a `500`
`application/problem+json` (`Startup.cs:211-236`) or as a truncated stream.

## `POST /ai/context/simulatesystemprompt`

Dry run of the RAG composition without calling the model (`ContextEndpoint.cs:17-55`, mounted at
`Startup.cs:246`). Policy `mlApi` (`ContextEndpoint.cs:26`).

Request (`ContextSimulateSystemPromptRequest`, both fields required):

```json
{ "ContextIdentifier": "support", "InitialPrompt": "How do I reset my password?" }
```

Response `200` (`ContextSimulateSystemPromptResponse`, `ContextEndpoint.cs:50-54`):

```json
{ "SystemPrompt": "…composed prompt…", "EstimatedTokenCount": 1842 }
```

`EstimatedTokenCount` is `systemPrompt.Length / 4` (integer floor division,
`ModelContextProvider.cs:65-68`) — an estimate for preview/logging, not a tokenizer result.

> The snapshot declares `200` **without a schema** (`Produces(StatusCodes.Status200OK)`,
> `ContextEndpoint.cs:28`), so generated clients type this response as untyped. The shape above is
> taken from `src/Ballware.Llm.Api/Model/ContextSimulateSystemPromptResponse.cs`.

`ContextEndpoint.cs:57-81` contains a private `HandleCreateEmbedding` that is **never mapped** — the
only mapped context operation is `simulatesystemprompt`.

## Job-trigger endpoints

All three schedule a Quartz job and return immediately; they do not wait for completion and do not
report job state back to the caller. Job semantics:
[../concepts/background-jobs.md](../concepts/background-jobs.md).

### `POST /ai/origin/addfromattachment`

Policy `mlApi` (`OriginEndpoint.cs:25`). Request (`OriginAddFromAttachmentRequest`, all four fields
required — `OriginAddFromAttachmentRequest.cs:7-16`):

| Field | Type | Meaning |
| --- | --- | --- |
| `OwnerEntity` | string | entity type owning the attachment in the Storage Service |
| `OwnerId` | uuid | id of the owning record |
| `Name` | string | display name for the created `Origin` |
| `AttachmentId` | uuid | attachment id in the Storage Service |

Triggers `JobKey("addfromattachment", "origin")` with `JobDataMap` keys `tenantId`, `userId`,
`ownerEntity`, `ownerId`, `attachmentId`, `name` (`OriginEndpoint.cs:58-67`).

**Status: `202 Accepted`** — `Results.Accepted()` (`OriginEndpoint.cs:69`). The snapshot declares `201`
because the endpoint metadata says `.Produces(StatusCodes.Status201Created)` (`OriginEndpoint.cs:27`).
Treat the declared `201` as drift.

### `POST /ai/origin/createembedding`

Policy `mlApi` (`OriginEndpoint.cs:35`). Request (`OriginCreateEmbeddingRequest`):

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `Ids` | array of uuid | yes in practice (null → `NullReferenceException`) | **origin** ids (`OriginEndpoint.cs:82`) |
| `Model` | string | **required** | embedding model to use |
| `Activate` | bool | no | whether the created `Embedding.Active` is `true` |
| `TopK` | int? | no | **never forwarded to the job** — see below |
| `TokensPerChunk` | int? | no | forwarded as `tokensPerChunk` when set (`OriginEndpoint.cs:92-95`) |
| `OverlapTokens` | int? | no | forwarded as `overlapTokens` when set (`OriginEndpoint.cs:97-100`) |

One job per id: `JobKey("createembedding", "origin")` (`OriginEndpoint.cs:102`).

`TopK` is accepted by the schema but the endpoint never puts it in the `JobDataMap`
(`OriginEndpoint.cs:84-100`) and the job never reads it, so `Embedding.top_k` stays `NULL` and
retrieval falls back to `Context:DefaultTopK` (default 5)
— see [../concepts/rag-context-and-tools.md](../concepts/rag-context-and-tools.md).

**Status: `202 Accepted`** — `Results.Accepted()` (`OriginEndpoint.cs:105`); the snapshot declares `201`
(`OriginEndpoint.cs:37`). Same drift as above.

### `POST /ai/embedding/recreate`

Policy `mlApi` (`EmbeddingEndpoint.cs:25`). Request (`RecreateEmbeddingRequest`):

| Field | Type | Meaning |
| --- | --- | --- |
| `Ids` | array of uuid | **embedding** ids (`EmbeddingEndpoint.cs:47`) |

One job per id: `JobKey("recreate", "embedding")` (`EmbeddingEndpoint.cs:55`).

**Status: `201 Created`** — `Results.Created()` (`EmbeddingEndpoint.cs:58`), matching the snapshot.

> Naming note: the class `RecreateEmbeddingRequest` lives in the file
> `src/Ballware.Llm.Api/Model/EmbeddingRecreateRequest.cs`; the snapshot and generated clients use the
> **class** name `RecreateEmbeddingRequest`.

## Not in OpenAPI

`GET /ai/hubs/chat` (SignalR) — see [signalr-chat-hub.md](signalr-chat-hub.md).
