# RAG context and tools

## System-prompt composition

`src/Ballware.Llm.Context/Internal/ModelContextProvider.cs` (scoped, `ServiceCollectionExtensions.cs:12`)
builds the prompt for a `(tenantId, contextIdentifier)` pair:

1. Load the `context` row by identifier (`ContextBaseRepository.cs:12-17`).
2. If the row is missing **or** `SystemPromptTemplate` is empty → return `string.Empty`
   (`ModelContextProvider.cs:21, 62`). There is no built-in template and no default knowledge block.
3. Global knowledge sources: `originRepository.GetGlobalOriginsAsync(tenantId)` — only
   `origin.global_context = true` (`OriginBaseRepository.cs:44-49`) — iterated
   `OrderBy(GlobalRelevance ?? 0)`, i.e. **ascending** (`ModelContextProvider.cs:25-27`).
4. For each origin, its **active** embeddings (`EmbeddingBaseRepository.cs:50-56`). Origins without an
   active embedding contribute nothing (`:32`).
5. For each active embedding, a block:

   ```text
   Source: <origin.Name>
   Contents:
   <chunk text>
   <chunk text>

   ```

   The retrieval query is the **initial user prompt**, embedded with that embedding's own model
   (`:39`), and the chunks come from
   `FetchTopKSimilarChunksAsync(tenantId, embedding.Id, queryVector, embedding.TopK ?? ContextOptions.DefaultTopK)`
   (`:41`, default 5 — `src/Ballware.Llm.Context/Configuration/ContextOptions.cs:5`).
   Chunks are appended verbatim: **no dedup, no score threshold, no separator**, and a blank line is
   emitted per origin (`:43-49`). Distances are discarded by the repository
   (`ChunkRepository.cs:23`), so nothing can rank or filter afterwards.
6. Templating via **SmartFormat.NET** (`:53-57`) with an anonymous object of exactly two properties, so
   the only placeholders are:

   | Placeholder | Content |
   | --- | --- |
   | `{GlobalContext}` | all knowledge blocks built above |
   | `{InitialPrompt}` | the caller's prompt text |

   Any other placeholder in the stored template is a template error, not a supported variable.

7. `ProvideWelcomeMessageAsync` returns `Context.WelcomeMessage ?? ""` (`:10-15`);
   `ProvideOptionsAsync` returns `{ Model = context.Model, NumCtx = context.NumCtx }` or an empty
   `ModelContextOptions` (both `null`) when the row is missing (`:70-84`).
8. `EstimateTokenCountAsync` is `systemPrompt.Length / 4` with integer floor division
   (`:65-68`); its `tenantId`/`contextIdentifier` parameters are unused. `ChatEndpoint`, `ChatSession`
   and `AiChatSession` use a different private copy, `(length + 3) / 4` (ceiling) — treat all token
   figures as rough logging values, never as billing figures.

`Embedding.MaxTokensPerChunk` / `OverlapTokens` are **ingest-time** settings and play no role in
retrieval; `Embedding.TopK` is the only per-embedding retrieval setting, and it is only ever populated
by the recreate job (see [knowledge-pipeline.md](knowledge-pipeline.md)).

> Asymmetry: prompt-time retrieval uses **global** origins (`GlobalContext = true`), while the
> `origins.list` tool lists **non-global** origins (`EmbeddingsToolProvider.cs:55` →
> `OriginBaseRepository.cs:51-56`) even though its description says "global".

## Model selection chain

```text
Context.Model            ModelContextProvider.cs:78
  → ChatContextOptions.Model   ChatSession.cs:27 / ChatEndpoint.cs:86
  → chatOptions.ModelId = contextOptions.Model ?? OpenAiInferenceOptions.Model   AiInferenceEngine.cs:57
  → configured Inference:Model  (nullable; OpenAiInferenceOptions.cs:7)
```

`Context.NumCtx` is forwarded as the additional property **`num_ctx`**, and only when non-null
(`AiInferenceEngine.cs:59-62`) — an Ollama-specific option; other OpenAI-compatible servers ignore it.
`ChatContextOptions` has exactly four fields (`Model`, `NumCtx`, `SystemPrompt`, `InitialHistory`);
temperature, max output tokens, stop sequences and timeouts are **absent**
(`src/Ballware.Llm.Inference/ChatContextOptions.cs:5-8`).

Endpoint URL construction (`AiInferenceEngine.cs:24-45`, duplicated in
`OpenAiEmbeddingEngine.cs:22-43`): a bare host becomes `<scheme>://<host>/v1`; a path not ending in
`/v1` gets `/v1` appended; a path already ending in `/v1` is left alone. The OpenAI client then appends
`chat/completions` (or `embeddings`).

## Inference session

`src/Ballware.Llm.Inference.OpenAi/Internal/AiInferenceEngine.cs` builds one shared
`ChatClient(...).AsIChatClient()` per engine instance (singleton) and per chat:
`new ChatClientBuilder(apiClient).UseFunctionInvocation().Build()` — the whole tool-call loop is delegated
to `Microsoft.Extensions.AI`; no logging/caching/retry stage is added.

`src/Ballware.Llm.Inference.OpenAi/Internal/AiChatSession.cs`:

- The system prompt is passed as `ChatOptions.Instructions` (`AiInferenceEngine.cs:101-104`), **never** as
  a message in the history.
- Requests contain `[history…, current user message]` (`AiChatSession.cs:167-184`); `System` messages in a
  supplied `InitialHistory` are dropped, as are empty ones (`:34-43`).
- Streaming deltas come from `GetStreamingResponseAsync` (`:74`) and are yielded unchanged
  (`:127-128`); empty deltas are skipped.
- Failure handling: `ClientResultException` with status 400/413 plus a message containing
  `maximum context length`, `input_tokens` or `context length` → warning `"LLM request exceeded context
  window."`; any other `ClientResultException` → error log; both `break` the loop and the original
  exception is rethrown (`:136-139`). **Single attempt: no retry, no truncation, no fallback text**, and
  the failed turn is **not** added to the history.
- History is per session object, in-memory (`:20`); `ChatRole` supports `System`/`User`/`Assistant` only
  and unknown roles map to `User` (`:191-200`).
- `options.Tools` is reassigned on **every** prompt (`:52`), mutating the shared `ChatOptions` instance.
- Tool-call artifacts are not inspected: the delta extraction is `response.ToString()` (`:186-189`), so
  function-call content is whatever the update's `ToString()` renders.
- Log messages refer to "Ollama API" although the client is OpenAI-compatible (`:95-112`) — log-text
  mismatch, not a transport fact. `OllamaToolRegistryInitializer` has the same legacy naming issue.

## Tool registry

`src/Ballware.Llm.Tools` defines `IToolProvider` (`Namespace`, `GetToolsForUser(tenantId, userId, claims)`),
`IToolRegistry` (static / scoped / MCP registration), `Tool` and `ToolParameter`
(`src/Ballware.Llm.Tools/Tool.cs`). Registration happens at host start via the hosted
tool-registration callback (`LlmHostingExtensions.cs:309-318`):

| Tool | Parameters | Provider | Source |
| --- | --- | --- | --- |
| `add`, `subtract` | `a`, `b` (`number`) | `MathToolProvider` (singleton) | `src/Ballware.Llm.Tools.Common/Internal/MathToolProvider.cs:11-46` |
| `origins.list` | none | `EmbeddingsToolProvider` (scoped) | `src/Ballware.Llm.Tools.Embedding/Internal/EmbeddingsToolProvider.cs:27-31` |
| `origins.content` | `originId` (string), `searchTerm` (string, optional), `maxChars` (integer, optional) | same | `:32-40` |
| `origins.query` | `query` (string), `originId` (string, optional), `topK` (integer, optional) | same | `:41-49` |

`IToolProvider.Namespace` (`math`, `embeddings`) is **never used** in tool naming
(`DefaultToolRegistry.cs:31-53`), so tools are exposed flat.

Tool behavior worth knowing:

- `origins.query`: `topK = clamp(value ?? 5, 1, 20)` (`:163`); without `originId` the search set is the
  **global** origins (`:184`); results are deduplicated (`Distinct()`, `:208-211`); per-model query
  vectors are cached within one call (`:192-206`).
- `origins.content`: `maxChars = clamp(value ?? 6000, 1000, 20000)` (`:91`); with `searchTerm`, up to
  6 snippets of 180 context characters plus image matches (`:103-134`); without, the content is truncated
  with a `… [gekürzt]` suffix (`:290`).
- Tool output strings are currently **German** for the embedding tools. These literals are model-facing,
  so they are part of what the LLM sees:
  - `Keine globalen Origins vorhanden.`
  - `Keine Treffer für '<term>'.`
  - `Parameter 'query' ist erforderlich.`
- **Required-parameters bug:** `ToolParameter` declares `bool required = false` as a constructor
  parameter but initializes the property to `false` and ignores it (`Tool.cs:3, 8`), so
  `AiCallbackTool.BuildJsonSchema` never emits a `"required"` array (`AiCallbackTool.cs:36-52`). Every
  tool parameter is optional in the schema the model sees.

## MCP servers

Configured per agent in `Mcp.Agents[]` (`src/Ballware.Llm.Hosting/Configuration/McpOptions.cs:12-27`):
`Namespace` (required), `ServiceUrl` (required), `UserTokenDelegation` (default `false`),
`CredentialIdentifier` (optional).

- Each agent gets a named `HttpClient` `mcp.{Namespace}` with `BaseAddress = ServiceUrl`
  (`LlmHostingExtensions.cs:165-167`); a client-credentials handler is attached only when
  `!UserTokenDelegation && CredentialIdentifier` is set (`:178-182`).
- Each agent is registered in the tool registry as `McpService("mcp.{Namespace}", UserTokenDelegation)`
  (`:314-317`).
- At chat start, `AiInferenceEngine.cs:70-97` resolves that client, uses its `BaseAddress` as the MCP
  endpoint, adds `Authorization: Bearer <user access token>` **only** when `UserTokenDelegation` is true
  and a token exists, then enumerates `mcpClient.ListToolsAsync()` and appends the tools.
  MCP clients are created per chat start and **never disposed** (no `await using`).
- `tenantId`, `userId`, `claims` are used only for tool resolution (`:66`); `userAccessToken` only for MCP
  auth. Nothing tenant-specific is sent to the inference endpoint.
- In DEBUG builds the MCP/storage/meta HTTP handlers accept any server certificate
  (`LlmHostingExtensions.cs:169-175, 189-194, 206-211`).

## Not implemented

Confirmed by search over `src/` (excluding `bin/`, `obj/`): no reranking or score threshold, no ANN index
(see [data-model.md](data-model.md)), no query expansion or hybrid search, no conversation persistence
outside a SignalR connection, no `IChatSessionBuilder`/`IChatSessionBuilderStage` implementation
(declared in `src/Ballware.Llm.Inference/` and unused), no `IModelTool`/`IModelToolProvider` implementation
(declared in `src/Ballware.Llm.Context/IModelToolProvider.cs` and unused), and no per-context tool
selection — every chat gets the same tool set.

## Related pages

- [knowledge-pipeline.md](knowledge-pipeline.md) — how the retrieved rows are produced
- [background-jobs.md](background-jobs.md) — job-driven ingestion/embedding paths
- [../api/llm-api-resources.md](../api/llm-api-resources.md) — the endpoints that consume this pipeline
