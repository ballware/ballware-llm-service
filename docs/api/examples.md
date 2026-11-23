# Worked examples

Every path, method, query parameter, field name and status code below exists in
[../openapi/llm.json](../openapi/llm.json) and in the source. Base URL `http://localhost:5006` is the
Development Kestrel endpoint (`src/Ballware.Llm.Service/appsettings.Development.json:15-21`); in the
Service container the exposed ports are 5000/5001 (`src/Ballware.Llm.Service/Dockerfile:3-4`). IDs are
placeholders.

## 1. Ingest a document, then embed it

### 1.1 Authenticate

Obtain a **user** token that carries the `mlApi` scope and a `tenant` claim
(`Startup.cs:105-112`, `AuthorizationOptions.cs:16-17, 26`). A token without the scope fails with `401`
before any handler runs. A token that is valid but has no `tenant` claim resolves to `Guid.Empty`
(`ballware-shared-dotnet/src/Ballware.Shared.Authorization/Internal/DefaultPrincipalUtils.cs:27-34`), so
the generic handlers answer `404` with the body `Tenant 00000000-0000-0000-0000-000000000000 not found`
(`EditingEndpointBuilder.cs:70-75`).

```bash
TOKEN=$(curl -s -X POST <tokenEndpoint> \
  -d 'grant_type=password&client_id=<clientId>&username=<user>&password=<password>&scope=openid mlApi' \
  | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])')
```

### 1.2 Start ingestion from a Storage Service attachment

```bash
curl -si -X POST "http://localhost:5006/ai/origin/addfromattachment" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "OwnerEntity": "document", "OwnerId": "3f1c0d2e-6b7a-4c9d-8e1f-2a3b4c5d6e7f", "Name": "Support FAQ", "AttachmentId": "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d" }'
```

```http
HTTP/1.1 202 Accepted          <-- Results.Accepted(), OriginEndpoint.cs:69 (the snapshot declares 201)
```

`POST /ai/origin/addfromattachment` triggers the Quartz job `("addfromattachment","origin")` and returns
immediately. It does **not** wait, and this job publishes **no** job-status record (its constructor has
no `IJobMetadataProvider` — `src/Ballware.Llm.Jobs/Internal/OriginAddFromAttachmentJob.cs:10-15`), so
there is nothing to poll in the Meta Service for this job.

### 1.3 Poll by reading the data

Poll the resource instead — poll `query` until the origin appears, then read it with `byid`:

```bash
curl -s "http://localhost:5006/ai/origin/query?identifier=primary&name=Support%20FAQ" \
  -H "Authorization: Bearer $TOKEN"
```

```json
[
  {
    "Id": "4b2f1a3c-9d8e-4f5a-6b7c-8d9e0f1a2b3c",
    "OwnerEntity": "attachment",
    "OwnerId": "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d",
    "ReferingEntity": "document",
    "ReferingId": "3f1c0d2e-6b7a-4c9d-8e1f-2a3b4c5d6e7f",
    "Name": "Support FAQ",
    "Description": "None",
    "GlobalContext": false,
    "GlobalRelevance": null,
    "ContentType": "text/markdown",
    "Content": null
  }
]
```

`Content` is empty here on purpose: `all`/`query` do not run the payload hook
(`TenantableBaseRepository.cs:106-116`). `byid` does:

```bash
curl -s "http://localhost:5006/ai/origin/byid?identifier=primary&id=4b2f1a3c-9d8e-4f5a-6b7c-8d9e0f1a2b3c" \
  -H "Authorization: Bearer $TOKEN"
```

```json
{
  "Id": "4b2f1a3c-9d8e-4f5a-6b7c-8d9e0f1a2b3c",
  "…": "same fields as above",
  "ContentType": "text/markdown",
  "Content": "# Support FAQ\n\n## Password reset\n…"
}
```

`OwnerEntity: "attachment"` and `Description: "None"` are values the job writes
(`OriginAddFromAttachmentJob.cs:53-54`), not client input.

### 1.4 Create the embedding

```bash
curl -si -X POST "http://localhost:5006/ai/origin/createembedding" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "Ids": ["4b2f1a3c-9d8e-4f5a-6b7c-8d9e0f1a2b3c"], "Model": "embeddinggemma", "Activate": true, "TokensPerChunk": 500, "OverlapTokens": 50 }'
```

```http
HTTP/1.1 202 Accepted          <-- Results.Accepted(), OriginEndpoint.cs:105 (the snapshot declares 201)
```

One job per id; `TopK` in the body would be accepted but ignored (`OriginEndpoint.cs:84-100`).

### 1.5 Confirm the embeddings

```bash
curl -s "http://localhost:5006/ai/embedding/all?identifier=primary" -H "Authorization: Bearer $TOKEN"
```

```json
[
  {
    "Id": "7c6b5a49-3d2e-4f1a-9b8c-7d6e5f4a3b2c",
    "OriginId": "4b2f1a3c-9d8e-4f5a-6b7c-8d9e0f1a2b3c",
    "Model": "embeddinggemma",
    "Active": true,
    "TopK": null,
    "MaxTokensPerChunk": null,
    "OverlapTokens": null
  }
]
```

`TopK`, `MaxTokensPerChunk` and `OverlapTokens` are `null` because `OriginCreateEmbeddingJob` does not
persist them (`OriginCreateEmbeddingJob.cs:64-70`); they are only filled by the recreate job
(`EmbeddingRecreateJob.cs:55-59`).

Chunks of that embedding:

```bash
curl -s "http://localhost:5006/ai/chunk/query?identifier=embedding&embedding=7c6b5a49-3d2e-4f1a-9b8c-7d6e5f4a3b2c" \
  -H "Authorization: Bearer $TOKEN"
```

The `embedding` identifier is the entity's own `ListQuery` scope
(`src/Ballware.Llm.Data.Ef/Repository/ChunkBaseRepository.cs:10-23`).

### 1.6 Recreate after changing chunk settings

```bash
curl -si -X POST "http://localhost:5006/ai/embedding/recreate" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "Ids": ["7c6b5a49-3d2e-4f5a-6b7c-8d9e0f1a2b3c"] }'
```

```http
HTTP/1.1 201 Created           <-- Results.Created(), EmbeddingEndpoint.cs:58
```

Note `recreate` takes **embedding** ids; `createembedding` takes **origin** ids.

## 2. Chat completions

### 2.1 Non-streaming

`model` is the `Context.Identifier` (`ChatEndpoint.cs:52-55, 73`), so `"model": "support"` chats with
the `context` row whose `identifier` is `support`.

```bash
curl -s -X POST "http://localhost:5006/ai/v1/chat/completions" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "model": "support", "messages": [ { "role": "user", "content": "How do I reset my password?" } ] }'
```

```json
{
  "id": "chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c",
  "object": "chat.completion",
  "created": 1764000000,
  "model": "support",
  "choices": [
    {
      "index": 0,
      "message": { "role": "assistant", "content": "Open your profile …" },
      "finish_reason": "stop"
    }
  ]
}
```

Non-`user` roles are flattened into the prompt as `"<role>: <content>"` (`ChatEndpoint.cs:112-119`):

```json
{ "model": "support", "messages": [ { "role": "system", "content": "Be brief." }, { "role": "user", "content": "Hello" } ] }
```

becomes the single prompt `system: Be brief.\nHello`.

### 2.2 Validation errors (`400`, plain text)

```bash
curl -si -X POST "http://localhost:5006/ai/v1/chat/completions" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "messages": [ { "role": "user", "content": "Hello" } ] }'
```

```http
HTTP/1.1 400 Bad Request
Content-Type: text/plain; charset=utf-8

The requested model must contain the context identifier.
```

Other bodies: `At least one message is required.` (empty `messages`) and
`At least one message with content is required.` (all contents blank) — `ChatEndpoint.cs:52-67`.
Missing/invalid token → `401` with an empty body.

### 2.3 Streaming (`stream: true`) — full SSE transcript

```bash
curl -sN -X POST "http://localhost:5006/ai/v1/chat/completions" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "model": "support", "stream": true, "messages": [ { "role": "user", "content": "How do I reset my password?" } ] }'
```

```text
data: {"id":"chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c","object":"chat.completion.chunk","created":1764000000,"model":"support","choices":[{"index":0,"delta":{"role":"assistant","content":null},"finish_reason":null}]}

data: {"id":"chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c","object":"chat.completion.chunk","created":1764000000,"model":"support","choices":[{"index":0,"delta":{"role":null,"content":"Open"},"finish_reason":null}]}

data: {"id":"chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c","object":"chat.completion.chunk","created":1764000000,"model":"support","choices":[{"index":0,"delta":{"role":null,"content":" your"},"finish_reason":null}]}

data: {"id":"chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c","object":"chat.completion.chunk","created":1764000000,"model":"support","choices":[{"index":0,"delta":{"role":null,"content":" profile …"},"finish_reason":null}]}

data: {"id":"chatcmpl-2f9c1b7a4d6e8f0a3b5c7d9e1f2a3b4c","object":"chat.completion.chunk","created":1764000000,"model":"support","choices":[{"index":0,"delta":{"role":null,"content":null},"finish_reason":"stop"}]}

data: [DONE]

```

Exact framing: `data: ` + JSON + `\n\n` per event (`ChatEndpoint.cs:228-235`), the terminator
`data: [DONE]\n\n` (`ChatEndpoint.cs:216-218`). One event per role, per delta, and one final event
(`ChatEndpoint.cs:154-214`). `id`, `created` and `model` are identical in every chunk.
`content`/`role`/`finish_reason` appear as `null` because the serializer runs with default options
(`ChatEndpoint.cs:232`), i.e. nulls are **not** omitted. Empty deltas are skipped, never emitted as
empty events (`ChatEndpoint.cs:174-177`).

## 3. Simulate a system prompt

```bash
curl -s -X POST "http://localhost:5006/ai/context/simulatesystemprompt" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "ContextIdentifier": "support", "InitialPrompt": "How do I reset my password?" }'
```

```json
{
  "SystemPrompt": "Source: Support FAQ\nContents:\n## Password reset\nOpen your profile …\n\n\nYou are a support assistant. Context:\n{…}\nQuestion: How do I reset my password?",
  "EstimatedTokenCount": 214
}
```

`SystemPrompt` is the composed text (knowledge block from `GlobalContext` origins + the template applied
by SmartFormat), and `EstimatedTokenCount` is `Length / 4` (`ModelContextProvider.cs:65-68`). The
`200` response has no schema in the snapshot — see [schemas.md](schemas.md).

For an identifier with no `context` row the composed prompt is the empty string and the count is `0`
(`ModelContextProvider.cs:21, 62`).

## 4. Export round-trip

```bash
curl -s -X POST "http://localhost:5006/ai/origin/exporturl?identifier=exportjson" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/x-www-form-urlencoded" \
  --data "name=Support%20FAQ"
```

```json
{ "TenantId": "1a2b3c4d-5e6f-4a5b-8c9d-0e1f2a3b4c5d", "Id": "6d5c4b3a-2f1e-4d0c-9b8a-7f6e5d4c3b2a" }
```

```bash
curl -sO "http://localhost:5006/ai/origin/download/1a2b3c4d-5e6f-4a5b-8c9d-0e1f2a3b4c5d/6d5c4b3a-2f1e-4d0c-9b8a-7f6e5d4c3b2a"
```

The download needs **no token** (`TenantantableEditingEndpoint.cs:126`); the record expires after one
day (`TenantableEndpointHandlerFactory.cs:275`), after which the download returns `404`. The
identifier `exportjson` is a convention example — the value must exist as an export definition in the
tenant's entity metadata (see
[../concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md)).
