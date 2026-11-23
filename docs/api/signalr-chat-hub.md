# SignalR chat hub

The interactive chat transport is a SignalR hub and is therefore **not** described in
[../openapi/llm.json](../openapi/llm.json). Everything below comes from
`src/Ballware.Llm.Api/Chat/ChatHub.cs` and `src/Ballware.Llm.Api/Chat/ChatSession*.cs`.

## Route and authentication

| Item | Value | Source |
| --- | --- | --- |
| Hub route | `/ai/hubs/chat` | `MapChatEndpoint("ai/hubs")` → `MapHub<ChatHub>(basePath + "/chat")` (`ChatEndpoint.cs:19-24`, `Startup.cs:244`) |
| Authorization | `[Authorize]` on the hub (`ChatHub.cs:12`) plus an explicit `IsAuthenticated` re-check in `OnConnectedAsync` (`ChatHub.cs:19-22`) | |
| Bearer token | `Authorization: Bearer <token>` **or** `?access_token=<token>` — the query parameter is honored only for paths starting with `/ai/hubs` (`Startup.cs:87-102`) | needed because browser WebSocket clients cannot set headers |
| SignalR services | `Services.AddSignalR()` (`Startup.cs:142`) | |

Unauthenticated connections fail with `UnauthorizedAccessException("User is not authenticated.")`
(`ChatHub.cs:21`).

## Client → server method

| Method | Signature | Behavior |
| --- | --- | --- |
| `Prompt` | `Prompt(string message)` (`ChatHub.cs:56`) | On the first call the inference session is created lazily (DI scope resolving `IInferenceEngine` and `IModelContextProvider`, `ChatHub.cs:62-69`); afterwards each streamed token is sent to the caller |

## Server → client events

| Event | Payload | Emitted |
| --- | --- | --- |
| `Response` | `string` | the welcome message on connect, **if** `Context.WelcomeMessage` for the context `"global"` is non-empty (`ChatHub.cs:41-46`); then **one event per streamed token** (`ChatHub.cs:71-74`) |
| `Error` | `string` | on any exception inside `Prompt` (`ChatHub.cs:76-81`) |

The hub streams: a prompt of N tokens produces N `Response` events. The error payload is currently a
localized string built from the exception message — the source literal is
`$"Fehler beim Verarbeiten der Anfrage: {ex.Message}"` (`ChatHub.cs:79`), i.e. German, and it is
rethrown afterwards, which terminates the hub call (`ChatHub.cs:80`).

## Session model

| Item | Value | Source |
| --- | --- | --- |
| Store | `ChatSessionStore`, process-wide **singleton**, `ConcurrentDictionary` keyed by `Context.ConnectionId` | `ChatSessionStore.cs:7-29`, registered at `Ballware.Llm.Api/ServiceCollectionExtensions.cs:10` |
| Session contents | `connectionId`, `contextIdentifier`, `tenantId`, `userId`, claims, and the raw `access_token` captured **once at connect time** | `ChatSession.cs:8-11` |
| Context identifier | hardcoded literal `"global"` (`ChatHub.cs:34`) — there is **no** seeding of a `global` `Context` row anywhere in the repo | |
| Chat lifetime | `sessionStore.Drop(ConnectionId)` on disconnect (`ChatHub.cs:49-54`) | |
| Multi-turn | the cached `IChatSession` accumulates user+assistant turns for the connection (`AiChatSession.cs:20`, replayed at `:171-179`); the system prompt and retrieved knowledge are computed **only from the first message** and never re-retrieved | |
| Persistence | none — in-memory only, no TTL, no size limit, nothing survives a process restart or a reconnect (a new `ConnectionId` starts a new session) | `ChatSessionStore.cs` |

If no `Context` row exists for `"global"`, the welcome message resolves to `""`
(`ModelContextProvider.cs:10-15`) and the system prompt to `string.Empty`
(`ModelContextProvider.cs:62`); model selection then falls back to `Inference:Model`
(`AiInferenceEngine.cs:57`).

## Comparison with the REST chat endpoint

| | `/ai/hubs/chat` | `POST /ai/v1/chat/completions` |
| --- | --- | --- |
| Transport | SignalR (WebSocket with fallbacks) | HTTP |
| Context identifier | fixed `"global"` | taken from `body.model` |
| Session | reused per connection, history accumulates | new session per request, no history |
| Streaming | `Response` event per token | SSE `chat.completion.chunk` events |
| Authorization policy | default (authenticated user) | default (authenticated user) |
| Documented in OpenAPI | no | yes |

Details: [llm-api-resources.md](llm-api-resources.md),
[../concepts/rag-context-and-tools.md](../concepts/rag-context-and-tools.md).
