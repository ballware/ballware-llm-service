# Chat session store — persistent LLM conversations

## Goal

Persist LLM chat conversations so a browser refresh or circuit reconnect no longer loses the
understanding. Add a user-bound session store (Postgres, same `LlmDbContext`) that supports
**start new / resume / rename / delete**, exposed

- in `Ballware.Llm.Service` as an HTTP API (generic `MapTenantableEditingApi` mount on `/ai/session`), and
- in `Ballware.Llm.App` as a chat-UI interaction (sidebar session list + new/rename/delete).

Access boundaries: a user only ever sees/edits/deletes **their own** sessions; a dedicated
administrative right (Meta Service Jint script `ai.session.admin`) unlocks all sessions of a tenant.

## Decisions (confirmed with the user)

| Topic | Decision |
|---|---|
| Storage shape | Two tables: `chat_session` + `chat_message` (append per turn, resumable, truncatable) |
| API form | Generic mount `MapTenantableEditingApi<Public.ChatSession>("/ai/session", "ai", "session", …)` — consistent with `context`/`origin`/… |
| Admin right | Evaluated through the existing Meta Service Jint checkers, candidate `ai.session.admin` |
| Persisting entry points | **Only the chat UI (`Ballware.Llm.App`)**. `ChatHub` and `POST /ai/v1/chat/completions` stay ephemeral |
| App write path | Direct repository access in the App host (same Postgres DB), no HTTP hop |
| Message visibility | History is returned **only on `byid`** (repository `ExtendById` hook, mirrors `Origin.Content`); `save` mutates metadata (title) only — messages are immutable through the API |
| Ownership enforcement | Repository-level owner filter from the `sub` claim; lifted only by `ai.session.admin` |
| Resume policy | Replay the last **N** messages into `ChatContextOptions.InitialHistory` (`Chat:MaxHistoryMessages`, default `50`); unlimited retention; hard delete |
| UI | Left sidebar (own sessions of the current context) + "Neue Unterhaltung" + rename + delete; route `/chat/{contextIdentifier}/{sessionId?}` |
| Creation timing | Lazy — the `chat_session` row is created with the **first user turn**, title derived from that question |
| "Edit" semantics | Auto title + rename; messages immutable |
| Context coupling | Session is bound to `context_identifier`; list is scoped to the context of the current route; removing a `context` row cascades deletion of its sessions |
| Rights in the App | App persistence is gated by the **owner filter only** (like today's chat endpoints); `ai.session.*` rights gate only the Service API mount |

## Verified context (drives the design)

- Two hosts share the wiring layer `Ballware.Llm.Hosting` and the same Postgres DB
  (`AddBallwareLlmDataStore`, `LlmHostingExtensions.cs:222-238`). The App has **no** HTTP API and reaches the
  model in-process (`Ballware.Llm.App/Chat/BallwareChatResponseProvider.cs`).
- Nothing is persisted today: `Ballware.Llm.Api/Chat/ChatSessionStore.cs` is a process-wide
  `ConcurrentDictionary` keyed by `Context.ConnectionId`, dropped on disconnect
  (`ChatHub.cs:34, 51`); the App's `BallwareChatScope.ConversationId` is a fresh `Guid` per circuit
  (`Ballware.Llm.App/Chat/BallwareChatScope.cs:13, 27, 36`) and is never resolved server-side.
- History exists in memory and is replayable: `IChatSession.History` and
  `ChatContextOptions.InitialHistory` (`Ballware.Llm.Inference/IChatSession.cs:5`, `ChatContextOptions.cs:8`,
  consumed at `Ballware.Llm.Inference.OpenAi/Internal/AiChatSession.cs:23-35, 171-186`).
  `InitialHistory` is currently used only to seed the welcome message
  (`BallwareChatResponseProvider.cs:167-172, 183`).
- Shared data layer has **tenant scoping only** — `ITenantableRepository<T>` never filters by user, and there is
  no `IOwnable`/`IUserScoped` concept in `ballware-shared-dotnet`. Owner filtering must therefore be implemented
  in this repo's repository subclass (`TenantableBaseRepository` seams: `ListQuery`, `ByIdQuery`,
  `ProduceNewAsync`, `ExtendByIdAsync`, `BeforeSaveAsync`, `RemovePreliminaryCheckAsync`, `BeforeRemoveAsync`;
  `SaveAsync`/`RemoveAsync` are interface implementations and are therefore overridable).
- `TenantableBaseRepository.SaveAsync` does `Mapper.Map(value, persistedItem)` (map-onto). Consequence:
  **`Public.ChatSession` must not expose `UserId`, `ContextIdentifier` or `Messages` as writable members**, or a
  client could re-home a session or rewrite its context/history.
- Rights model today: candidate `ai.<entity>.<right>` evaluated by tenant + entity Jint scripts fed from Meta
  Service metadata (`MetaServiceMetadataAdapter.cs`, `MetaServiceLlmMetadataProfile.cs:10-11`). Missing entity
  metadata ⇒ `404 Entity <entity> not found for tenant <id>`. Chat endpoints are **not** rights-checked
  (`ChatEndpoint.cs:29`, `ChatHub.cs:12`) — the App stays in that category.
- Cascade precedent without FKs: `OriginBaseRepository.BeforeRemoveAsync` deletes embeddings/chunks/images
  (`Ballware.Llm.Data.Ef/Repository/OriginBaseRepository.cs:11-42`). Use the same shape for `context → chat_session`.
- DevExpress AI Chat v26.1 offers the persistence hooks we need: `IAIChat.SaveMessages()` /
  `LoadMessages(IEnumerable<BlazorChatMessage>)`, `MessageSending`, `ResponseReceived`
  (`DevExpress.AIIntegration.Blazor.Chat.DxAIChat`, docs 405973 / `…DxAIChat.SaveMessages`).

## Data model

Both persistables follow the shared shape (`IEntity` + `ITenantable` + `IAuditable`, `long? Id` surrogate key,
`Guid Uuid` public id, UTC-normalized stamps via `LlmModelBaseCustomizer`).

### `chat_session` (`Ballware.Llm.Data/Persistables/ChatSession.cs`, `[Table("chat_session")]`)

| Column | Type | Notes |
|---|---|---|
| `id` | `bigint` identity | PK, never exposed |
| `uuid` | `uuid` | public id ⇄ `Public.ChatSession.Id` |
| `tenant_id` | `uuid` | NOT NULL |
| `user_id` | `uuid` | **owner**, NOT NULL — set from the acting user, never client-supplied |
| `context_identifier` | `varchar(100)` | NOT NULL, matches `context.identifier` (no FK) |
| `title` | `varchar(200)` | NULL until first turn |
| `creator_id`, `last_changer_id` | `uuid` NULL | audit |
| `create_stamp`, `last_change_stamp` | `timestamptz` NULL | audit |

Indexes: unique `(tenant_id, uuid)`, `(tenant_id)`, `(tenant_id, user_id)`,
`(tenant_id, user_id, context_identifier)`, `(tenant_id, context_identifier)`.

### `chat_message` (`Ballware.Llm.Data/Persistables/ChatMessage.cs`, `[Table("chat_message")]`)

| Column | Type | Notes |
|---|---|---|
| `id` | `bigint` identity | PK |
| `uuid` | `uuid` | public id |
| `tenant_id` | `uuid` | NOT NULL |
| `session_id` | `uuid` | NOT NULL → `chat_session.uuid`, **no FK** (repo convention) |
| `role` | `varchar(20)` | NOT NULL, literals `system` / `user` / `assistant` (persist only `user` + `assistant`) |
| `content` | `text` | NOT NULL |
| `sequence` | `int` | NOT NULL, 1-based append order |
| audit columns | as above | `creator_id` = owner |

Indexes: unique `(tenant_id, uuid)`, unique `(tenant_id, session_id, sequence)`, `(tenant_id)`,
`(tenant_id, session_id)`.

### Public contracts (`Ballware.Llm.Data/Public/`)

```csharp
public class ChatSession : IEditable
{
    public Guid Id { get; set; }
    public string ContextIdentifier { get; set; }   // read-only semantics: stripped on save
    public string? Title { get; set; }              // the only writable field
    public Guid? OwnerUserId { get; set; }          // Persistable → Public mapping only (ignored inbound)
    public DateTime? CreateStamp { get; set; }
    public DateTime? LastChangeStamp { get; set; }
    public List<ChatSessionMessage>? Messages { get; set; }   // null on all/query, filled on byid
}

public class ChatSessionMessage
{
    public string Role { get; set; }        // "user" | "assistant"
    public string Content { get; set; }
    public int Sequence { get; set; }
    public DateTime? CreateStamp { get; set; }
}
```

No `UserId` member on the DTO: ownership can never be written through the API.

## Component layout

| Concern | Location |
|---|---|
| Persistables / Public DTOs | `Ballware.Llm.Data/Persistables/ChatSession.cs`, `…/ChatMessage.cs`, `Ballware.Llm.Data/Public/ChatSession.cs`, `…/ChatSessionMessage.cs` |
| Repository contract | `Ballware.Llm.Data/Repository/IChatSessionRepository.cs` (extends `ITenantableRepository<Public.ChatSession>`) |
| Actor + guard contracts (no heavy deps) | `Ballware.Llm.Data/Repository/ChatSessionActor.cs` (`record ChatSessionActor(Guid UserId, bool IsAdmin)`), `Ballware.Llm.Data/Repository/IChatSessionActorResolver.cs`, `Ballware.Llm.Data/ChatSessionNotVisibleException.cs` |
| EF implementation | `Ballware.Llm.Data.Ef/Repository/ChatSessionBaseRepository.cs` |
| DbContext / customizer / mappings / DI | `Ballware.Llm.Data.Ef.Postgres/LlmDbContext.cs`, `Ballware.Llm.Data.Ef/Model/LlmModelBaseCustomizer.cs`, `Ballware.Llm.Data.Ef/Mapping/StorageMappingProfile.cs`, `Ballware.Llm.Data.Ef.Postgres/ServiceCollectionExtensions.cs` |
| Admin-right resolution (Meta Service + Jint) | `Ballware.Llm.Hosting/Adapter/MetaServiceChatSessionActorResolver.cs`, registered in `LlmHostingExtensions.AddBallwareLlmMetadataAndStorageAdapters` |
| API mount | `Ballware.Llm.Service/Startup.cs` (single line next to `:250-254`) + exception→status mapping in the existing handler (`:135-160`) |
| Chat UI + persistence | `Ballware.Llm.App/Components/Pages/Chat.razor`, `Ballware.Llm.App/Chat/BallwareChatScope.cs`, `Ballware.Llm.App/Chat/BallwareChatResponseProvider.cs`, new `Ballware.Llm.App/Chat/ChatSessionOptions.cs` |

## Ownership and rights

Actor resolution (`IChatSessionActorResolver`, implemented in Hosting):

1. `userId` = acting user (`Guid? userId` from the shared `UserId` binding on write paths; otherwise
   `claims[Authorization:UserIdClaim]`, default claim `sub`).
2. `IsAdmin` = two-stage right check for candidate **`ai.session.admin`** via
   `IAuthorizationMetadataProvider` + `ITenantRightsChecker` + `IEntityRightsChecker`
   (recipe: `Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs:9-38`). Missing metadata or
   script denial ⇒ `IsAdmin = false` (never an error).

Enforcement matrix (all inside `ChatSessionBaseRepository`, applies to both the App path and the API mount):

| Operation | Rule |
|---|---|
| `AllAsync` / `QueryAsync` | `Where(t => t.TenantId == tenantId && (actor.IsAdmin \|\| t.UserId == actor.UserId))` in the `ListQuery` override; `identifier=primary` lists own sessions of one context (`?context=<identifier>` query param), admin sees all |
| `ByIdAsync` | `ByIdQuery` adds the same predicate; foreign session ⇒ `404` (existing semantics) |
| `NewAsync` / `NewQueryAsync` | `ProduceNewAsync` stamps `UserId = actor.UserId` (even for admins: created rows always belong to the acting user) |
| `SaveAsync` (override) | visibility pre-check; not visible ⇒ `ChatSessionNotVisibleException` → `404`; applies **title only** (incoming `ContextIdentifier`/`Messages` are dropped); `UserId` untouched |
| `RemoveAsync` (override) | visibility pre-check, then `BeforeRemoveAsync` deletes the session's `chat_message` rows, then the session row |
| `ExtendByIdAsync` | fills `Messages` for `byid` only (latest `Chat:MaxHistoryMessages` … full history cap `500`) |
| `context` removal | `ContextBaseRepository` gains an `IChatSessionRepository` constructor dependency and deletes sessions + messages for `TenantId + Identifier` in `BeforeRemoveAsync` (mirrors `OriginBaseRepository.cs:11-42`) |

App path: no right checks, owner filter only (decision). API path: the mount's built-in cascade adds
`ai.session.view` / `.add` / `.edit` / `.delete` on top.

## API surface (`Ballware.Llm.Service`)

Add to `Startup.InitializeApp` after `Startup.cs:254`:

```csharp
app.MapTenantableEditingApi<Data.Public.ChatSession>("/ai/session", "ai", "session", "Session", "Session", "mlApi", "llm");
```

Resulting operations (all `RequireAuthorization("mlApi")` + right candidate `ai.session.<right>`):

| Route | Use in this feature |
|---|---|
| `GET /ai/session/all?identifier=primary[&context=<ctx>]` | list own sessions (admin: all) |
| `GET /ai/session/query?identifier=primary&…` | filtered list (owner-scoped) |
| `GET /ai/session/new?identifier=primary` | metadata stub with owner stamped (UI does not need it — sessions are created lazily) |
| `GET /ai/session/byid?identifier=primary&id=<uuid>` | session **including** `Messages` |
| `POST /ai/session/save?identifier=primary` | rename (`{ "Id": …, "Title": … }`) |
| `POST /ai/session/savebatch?identifier=primary` | bulk rename |
| `DELETE /ai/session/remove/{id}` | delete session + messages |
| `POST /ai/session/import`, `GET /export`, `POST /exporturl`, `GET /download/{tenantId}/{id}` | mounted by the shared helper but **unsupported for sessions**: no Quartz `JobKey.Create("import", "session")` and no export definition. Deny in tenant metadata (`ai.session.import`, export identifiers) and document it |

Status semantics: `401` scope/right denied, `404` tenant/entity/item not found (incl. foreign-owner sessions),
`400` remove preconditions. Add a `ChatSessionNotVisibleException` → `404` case in the existing exception
handler (`Startup.cs:135-160`) so repository rejections never surface as `500`.

`ChatHub`, `ChatSessionStore` and `POST /ai/v1/chat/completions` are **unchanged** (explicit scope decision).

## Chat UI behavior (`Ballware.Llm.App`)

Route: `@page "/chat/{contextIdentifier}/{sessionId?}"`.

1. `OnInitializedAsync` (existing validation, `Chat.razor:110-157`) additionally:
   - `ChatScope.Initialize(user, tenantId, userId, contextIdentifier)` unchanged;
   - if `sessionId` present → `IChatSessionRepository.ByIdForUserAsync(tenantId, userId, sessionId)`;
     missing/foreign/other-context session ⇒ validation error "Unterhaltung nicht verfügbar." and no chat;
   - `IChatSessionRepository.ListByUserAsync(tenantId, userId, contextIdentifier, Chat:SessionListLimit)` →
     sidebar model;
   - `ChatScope.SetSession(sessionIdOrNull)`.
2. `DxAIChat`: keep `UseStreaming`, `ChatResponseProviderServiceKey`; add `@key="sessionKey"`
   (session id or `"new"`) so switching sessions rebuilds the component; `Initialized` handler
   `LoadMessages(...)`:
   - new session → only the context welcome message (as today, `Chat.razor:159-167`);
   - resumed session → persisted messages, welcome message **not** replayed and **not** persisted.
3. Sidebar (own sessions of the current context): title + `LastChangeStamp`, "Neue Unterhaltung",
   inline rename (`RenameAsync`), "Löschen" (`DeleteForUserAsync`). Session switching uses
   `NavigationManager.NavigateTo(url, forceLoad: true)` so a fresh circuit + fresh scoped
   `BallwareChatResponseProvider` is created — this is what prevents a stale in-memory `chatSession`
   (`BallwareChatResponseProvider.cs:32`) from leaking across sessions. Extend the flex layout CSS in
   `Chat.razor:42-98` for the sidebar column.
4. Turn persistence in `BallwareChatResponseProvider` (`EnsureChatSessionAsync` + `GetResponseAsync`):
   - **resume**: load `GetMessagesAsync(tenantId, userId, sessionId, Chat:MaxHistoryMessages)` into
     `ChatContextOptions.InitialHistory` (`BallwareChatResponseProvider.cs:163-188`) instead of the
     welcome-message seed;
   - **first turn**: `CreateAsync(tenantId, userId, contextIdentifier, AutoTitle(prompt))` where `AutoTitle`
     truncates the first user message to 100 chars (fallback `"Unterhaltung"`), then append the turn;
   - **after each completed turn**: `AppendMessagesAsync` with the user prompt + the concatenated streamed
     assistant text. On `OperationCanceledException` persist the partial assistant text **only if** tokens were
     already streamed (that is what the user saw); on error persist the user message plus nothing, matching the
     current error-update behavior.
   - Persistence failures must not break the UI stream: log (`ILogger`) and continue.
5. `BallwareChatScope`: add `Guid? SessionId`, `bool HasSession`, `SetSession(…)`, extend `Reset()`/`Initialize()`;
   set `ConversationId = sessionId.ToString("N")` when resuming so the provider's `conversationId` is stable.

Configuration (`Ballware.Llm.App/appsettings.json`, in-code defaults so existing deployments keep working):

```jsonc
"Chat": { "MaxHistoryMessages": 50, "SessionListLimit": 50 }
```

## Implementation order

1. **Data layer** — persistables, public DTOs, `IChatSessionRepository`, actor/exception contracts,
   `StorageMappingProfile` entries (`Uuid ↔ Id`, `Ignore(dest => dest.Id!)`, `OwnerUserId` one-directional).
2. **EF wiring** — `DbSet`s in `LlmDbContext.cs`; extend `LlmModelBaseCustomizer<…>` generics with
   `TChatSession, TChatMessage` (keys + the indexes above, descending `last_change_stamp` allowed via
   `IsDescending`) and update the `ReplaceService<…>` generic list in
   `Ballware.Llm.Data.Ef.Postgres/ServiceCollectionExtensions.cs:28`; register
   `ITenantableRepository<Public.ChatSession>` + `IChatSessionRepository` → `ChatSessionBaseRepository`.
3. **Repository semantics** — owner filter in `ListQuery`/`ByIdQuery`, owner stamping in `ProduceNewAsync`,
   `SaveAsync`/`RemoveAsync` overrides, message cascade in `BeforeRemoveAsync`, `ExtendByIdAsync` history fill,
   `ListByUserAsync`/`ByIdForUserAsync`/`CreateAsync`/`AppendMessagesAsync`/`GetMessagesAsync`/`RenameAsync`/
   `DeleteForUserAsync`/`DeleteByContextAsync`. Append `sequence = MAX(sequence)+1` inside one transaction.
4. **Admin right resolution** — `MetaServiceChatSessionActorResolver` in Hosting + DI registration; optional
   constructor injection into `ChatSessionBaseRepository` (null ⇒ `IsAdmin = false`).
5. **Context cascade** — inject `IChatSessionRepository` into `ContextBaseRepository` and delete sessions/messages
   for the removed context (`IChatSessionRepository` must not depend on `IContextRepository`: no cycle).
6. **Migration** — `dotnet ef migrations add AddChatSessionStore -p src/Ballware.Llm.Data.Ef.Postgres -s src/Ballware.Llm.Service`
   (commit the generated pair + snapshot). Both hosts ship `Llm.AutoMigrations = false`
   (`Ballware.Llm.App/appsettings.json:62`), so apply once against the shared DB
   (`dotnet ef database update …` or `Llm.AutoMigrations = true` in dev).
7. **API mount** — add the mount line in `Ballware.Llm.Service/Startup.cs` and the `404` mapping for
   `ChatSessionNotVisibleException`.
8. **Chat UI** — scope/provider persistence, sidebar + rename/delete, route change, `Chat` options, German UI
   strings consistent with `Chat.razor:119, 130, 155`.
9. **Docs** — `docs/concepts/data-model.md` (two new tables + index list + cascade), `docs/api/generic-crud.md`
   (sixth resource, session-specific `save`/`byid` semantics, unsupported import/export),
   `docs/concepts/tenancy-and-authorization.md` (owner filter + `ai.session.admin`),
   `docs/concepts/chat-application.md` (persistence flow, new route), `docs/api/metadata-templates.md`
   (`session` entity + `ai.session.admin` script pattern), `docs/README.md`; refresh the OpenAPI snapshot with
   `docs/openapi/fetch-spec.sh` (+ `docs/openapi/manifest.json`).

## Deployment prerequisites

- Meta Service must define entity `session` for application `ai` in each tenant that uses the API, otherwise
  **every** `/ai/session/*` operation returns `404 Entity session not found for tenant <id>`. Suggested tenant
  script additions (documented in `docs/api/metadata-templates.md`):

  ```javascript
  const owned = (userinfo.right || []);
  if (right.startsWith('ai.session.')) {
    if (right === 'ai.session.admin') return owned.includes('ai.session.admin');
    return owned.includes('ai.session.view') || owned.includes('ai.session.admin');   // view/all/query/byid
  }
  ```

  …with the same pattern for `.add`/`.edit`/`.delete`, and `false` for import/export identifiers.
- Database migration applied to the shared Postgres DB before either host starts.
- No change to `ChatHub` clients; existing OpenAI-completions clients unaffected.

## Risks / gotchas

1. **Ownership takeover via `save`** — mitigated structurally: no `UserId` on the DTO, `ContextIdentifier` and
   `Messages` dropped before the map-onto, `UserId` never re-assigned.
2. **`ExtendById` fills history only on `byid`** (`all`/`query` return `Messages = null`) — mirrors
   `Origin.Content`; document it so clients do not read `null` as "empty conversation".
3. **Concurrency**: two circuits appending to one session race on `sequence`; unique
   `(tenant_id, session_id, sequence)` surfaces it. Accepted: last writer wins, no optimistic locking in v1.
4. **Context-window overflow on resume** — 50 messages plus a large system prompt can exceed `num_ctx`;
   `AiChatSession.IsContextLengthError` already maps that to the existing UI error
   (`AiChatSession.cs:230-241`). Tunable via `Chat:MaxHistoryMessages`.
5. **No FKs**: manual SQL deletes of `chat_session` orphan `chat_message` rows (same caveat as the existing
   `origin`/`embedding`/`chunk` model, `docs/concepts/data-model.md:166-169`). Only the API/repository path
   cascades.
6. **`ChatHub` and the OpenAI endpoint stay ephemeral** by decision — do not advertise persisted history for
   those transports in docs.
7. **Scoped provider vs. Blazor navigation**: scoped services are per circuit, not per page. `forceLoad: true`
   navigation (step 8) is what guarantees a fresh `BallwareChatResponseProvider.chatSession`.
8. Renaming through the App writes `LastChangerId`/`LastChangeStamp` via the shared audit path
   (`TenantableBaseRepository.SaveAsync` semantics) — the sidebar sorts on `last_change_stamp`, so renames
   reorder the list. Expected.

## Validation

- `dotnet build ballware-llm-service.sln` — the customizer generic arity change touches
  `Data.Ef.Postgres/ServiceCollectionExtensions.cs` and must compile in both hosts.
- New test project `test/Ballware.Llm.Data.Ef.Tests` (NUnit + Testcontainers Postgres, mirroring
  `test/Ballware.Llm.Ingestion.Docling.Tests`), covering:
  1. user A's `all`/`query`/`byid` never return user B's session; `save`/`remove` on B's session ⇒ not visible;
  2. `ChatSessionActor(IsAdmin: true)` sees/edits/deletes foreign sessions;
  3. first turn creates the session with the derived title; subsequent turns append with increasing `sequence`;
  4. `GetMessagesAsync(…, maxCount: 3)` returns the latest 3 in ascending order (resume order);
  5. deleting a `context` removes its sessions and their messages; deleting a session removes its messages;
  6. a forged `ChatSessionMessage`/`ContextIdentifier` payload through `SaveAsync` cannot alter ownership,
     context or history.
- Manual UAT (`Ballware.Llm.App`, `/chat/global`): start a chat → several turns → browser refresh → session
  reappears in the sidebar and resumes with the full tail → rename → new session → delete → confirm the row and
  its `chat_message` rows are gone (`SELECT … FROM chat_session/chat_message`).
- Manual API UAT (`Ballware.Llm.Service`, `mlApi` token): `all` → own sessions only; `byid` on another user's
  session → `404`; `save` rename; `remove/{id}`; repeat with a token carrying `ai.session.admin` in tenant
  metadata and confirm cross-user visibility. Confirm `import`/`export` are denied by metadata.

## Out of scope (explicit)

- Retention/TTL job, archiving, soft delete, per-message deletion, session sharing/export formats.
- Session persistence for `ChatHub` and `/ai/v1/chat/completions`.
- An admin UI for session administration (API-only for now).
- Multi-tenant (`GetUserTenantIds`) and cross-context session migration.
