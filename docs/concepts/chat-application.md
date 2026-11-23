# Chat application (`Ballware.Llm.App`)

A second host in the same solution: a Blazor Server chat UI around the same context/inference stack. It
shares the wiring layer (`Ballware.Llm.Hosting`) but exposes **no** API, **no** Swagger and **no** jobs.

## Host wiring

`src/Ballware.Llm.App/Program.cs` mirrors the Service's configuration order
(`appsettings.json` → `appsettings.{Environment}.json` → `appsettings.local.json` → environment
variables, `:12-16`), Serilog (`:18-23`), and Kestrel from the `Kestrel` section (`:25`).

`src/Ballware.Llm.App/Startup.cs`:

| Stage | Detail |
| --- | --- |
| Core wiring | `AddBallwareLlmCoreConfiguration` (`:70`), then `AddBallwareLlmClients`, `AddBallwareLlmDataStore`, `AddBallwareLlmSharedAuthorizationUtils`, `AddBallwareLlmMetadataAndStorageAdapters`, `AddBallwareLlmTools`, `AddBallwareLlmContextAndInference` (`:100-105`) |
| Browser auth | `Services.AddBallwareBrowserAuthorization(Configuration, sessionOptions)` (`:82`) — the whole cookie + OIDC stack lives in the external packages `Ballware.Shared.Web.Oidc` / `Ballware.Shared.Web.Session` `1.3.0-alpha.4` (`Ballware.Llm.App.csproj:15-16`); `MapInboundClaims = false` for the OpenID Connect scheme (`:84-86`) |
| Session | `Session` section: cookie `.Ballware.Llm.Session`, cache key prefix `llm:session:`, 60 min expiry, default session cookie suppressed, data-protection purpose `Ballware.Llm.Session.ClaimsPrincipal` (`appsettings.json:41-48`, in-code fallbacks `Startup.cs:72-80`) |
| UI | `AddRazorComponents().AddInteractiveServerComponents(...)` with `MaximumReceiveMessageSize = 256 * 1024` for the Blazor circuit hub (`:90-92`), `AddDevExpressBlazor()` (`:108`), `AddDevExpressAI()` (`:109`) |
| Framing | `ContentSecurity.FrameAncestors` (`appsettings.json:39`) normalized to a CSP frame-ancestors value (`Startup.cs:30, 34-66`), passed to `AddInteractiveServerRenderMode` (`:188-192`); when foreign origins are allowed, `Antiforgery.SuppressXFrameOptionsHeader = true` (`:94-98`) |
| Culture | `CurrentCulture`/`CurrentUICulture` forced to `de-DE` (`:179-184`) |
| Middleware order | `UseForwardedHeaders (XForwardedFor \| XForwardedProto)` → `UseRouting` → `UseBallwareWebSession` → `UseBallwareChallengeDifferentiation` → `UseAuthentication` → `UseBallwareSignInChallengeFallback` → `UseAuthorization` → `UseAntiforgery` → `UseStaticFiles` (`:162-177`) |
| Endpoints | exactly two: `app.MapBallwareSignOnEndpoint()` (`:186`) and `app.MapRazorComponents<App>().AddInteractiveServerRenderMode(...)` (`:188-192`). No `MapControllers`, no `MapHub`, no Swagger |

The App warns at startup when `Authorization:PublicBaseUrl` is unset, because then the OIDC
`redirect_uri` is derived from the incoming request and **every** host the app is reached with must be
registered as a valid redirect URI for its callback path at the IdP (`:122-127`). The callback path is the
ASP.NET Core default `/signin-oidc` — the App never sets `CallbackPath`, and `/signin`, `/logout` or
`/callback` do not appear as literal routes in this repo. Sign-out/session revocation is not implemented.

## Routes

| Route | Behavior | Source |
| --- | --- | --- |
| `/chat/{contextIdentifier?}` | `[Authorize]` interactive chat page: resolves tenant/user claims, loads the `context` row via `IContextRepository.GetByIdentifierAsync`, pre-loads the welcome message, initializes the chat scope, renders `DxAIChat` | `Components/Pages/Chat.razor:1-3, 34-38, 123-150` |
| `/chat` (parameter absent) | rejected — the chat is never rendered | `Chat.razor:104, 117-121` |
| package sign-on endpoint | cookie sign-on from an externally obtained `idToken`, mapped by `MapBallwareSignOnEndpoint()`; redirect prefixes restricted to `/chat` (`Authorization.SignOnAllowedRedirectPathPrefixes`) | `Startup.cs:186`, `appsettings.json:56-57` |
| `/_blazor` | Blazor Server circuit hub only (SignalR is not used for LLM traffic) | `Startup.cs:90-92` |
| anything else | "not found" content; there is no index page (`Index.razor` absent) | `Components/Routes.razor:11-15` |

UI text is currently **German** (`lang="de"`, `Components/App.razor:2`; validation strings in
`Chat.razor:29, 130, 155` and `Routes.razor:5-7`).

## How it reaches the model

Not through `Ballware.Llm.Service`, not through the SignalR chat hub, and not through HTTP at all: the App
uses the in-process `IInferenceEngine`, which talks to the configured OpenAI-compatible endpoint.

| Step | Source |
| --- | --- |
| `DxAIChat` is bound to a keyed `IChatResponseProvider` | `Startup.cs:113-115` (key `"ballware-chat"`, `BallwareChatScope.cs:7`), `Chat.razor:37, 101` |
| The provider injects `IInferenceEngine`, `IModelContextProvider`, `IPrincipalUtils`, `AuthenticationStateProvider`, `IHttpContextAccessor` | `Chat/BallwareChatResponseProvider.cs:22-30` |
| Session created once per chat scope, with the **welcome message seeded as an assistant message** into `InitialHistory` — the only place `InitialHistory` is ever populated | `BallwareChatResponseProvider.cs:149-191`, esp. `:167-172, 183` |
| Only the last user message is sent as the prompt | `BallwareChatResponseProvider.cs:202-233` |
| Streaming consumed from `IChatSession.PromptStreamingAsync` and re-emitted as DevExpress chat updates | `BallwareChatResponseProvider.cs:92, 129` |
| The actual HTTP hop is the OpenAI client inside `AiInferenceEngine` | `src/Ballware.Llm.Inference.OpenAi/Internal/AiInferenceEngine.cs:14-17, 24-45` |
| The user's access token is forwarded for MCP delegation | `BallwareChatResponseProvider.cs:161, 242` |

Multi-turn: stateful per chat scope (the `IChatSession` history), with `ConversationId` regenerated on
reset/initialize (`BallwareChatScope.cs:13, 27, 36`) and used only as a UI-level id, never for a
server-side lookup.

## Configuration

`src/Ballware.Llm.App/appsettings.json` (tracked) supplies: `Kestrel.Endpoints.Http.Url = "http://+:3004"`
(`:5`) with request-size limits (`:9-10`), `ContentSecurity.FrameAncestors` (`:39`), `Session` (`:41-48`),
`Authorization` (`Authority`, `Audience`, `ClientId`, `RequiredUserScope = mlApi`,
`UserScopePolicyName = mlApi`, `ServiceScopePolicyName = serviceApi`, `SignOnAllowedHosts = []`,
`SignOnAllowedRedirectPathPrefixes = ["/chat"]`, `RequireHttpsMetadata`) (`:49-59`),
`Llm.Provider = postgres` / `Llm.AutoMigrations = false` (`:61-62`), `Inference` and `Embedding`
(`Endpoint`, `ApiKey`, `Model`) (`:64-72`), `MetaClient` / `StorageClient` (`ServiceUrl`,
`CredentialIdentifier`) (`:74-81`), `ConnectionStrings.LlmStorageConnection` (`:83`), `AllowedHosts` (`:85`).

Not present in the tracked file (so they must come from `appsettings.local.json` or environment
variables): `ServiceCredentials` (referenced by `MetaClient.CredentialIdentifier` /
`StorageClient.CredentialIdentifier`), `Context`, `Chunking`, `Cors` (therefore **no CORS policy** is
registered for the App), `Mcp` (so no MCP agents), `Authorization:TenantClaim`/`UserIdClaim`/`RightClaim`
(defaults `tenant`/`sub`/`right`, `src/Ballware.Llm.Hosting/Configuration/LlmAuthorizationClaimsOptions.cs:5-9`),
`DetailedErrors` (falls back to `IsDevelopment`, `Startup.cs:88`).

`appsettings.Development.json` is declared in `Ballware.Llm.App.csproj:30-32` but **does not exist** in
the project. Secrets (client secrets, API keys, the database password) live only in the git-ignored
`appsettings.local.json` and are not reproduced here.

## Differences vs. `Ballware.Llm.Service`

| Aspect | `Ballware.Llm.App` | `Ballware.Llm.Service` |
| --- | --- | --- |
| Authentication | cookie + OIDC browser sign-in (`AddBallwareBrowserAuthorization`, `Startup.cs:82`) | JWT bearer only (`Service/Startup.cs:71-103`) |
| Policies in code | none — scope/policy names are consumed by the external Oidc package from config (`appsettings.json:53-55`) | `mlApi` + `serviceApi` built in code (`Service/Startup.cs:105-119`) |
| HTTP API | none | 60 paths + SignalR hub |
| Swagger | absent | `llm` + empty `service` |
| SignalR | Blazor circuit only | `ChatHub` at `/ai/hubs/chat` |
| Quartz jobs | none | 5 job keys |
| Docling / ingestion | not referenced | `DoclingOptions` + ingestion + `Docling.Client` |
| Job adapters | not registered | `IJobMetadataProvider`, `IJobFileStorageAdapter` |
| CORS | not registered | `app.UseCors()` (`Service/Startup.cs:238`) |
| JSON naming | not configured | PascalCase (`Service/Startup.cs:121-129`) |
| Culture | forced `de-DE` | not configured |
| Kestrel | `http://+:3004` | Development `http://localhost:5006` |
| Project references | only `Ballware.Llm.Hosting` (`Ballware.Llm.App.csproj:23`); transitively Api, Context, Data*, Embedding*, Inference*, Metadata, Tools* — **not** Service, Jobs, Ingestion, Docling.Client | all of them (`Ballware.Llm.Service.csproj:53-67`) |
| DevExpress packages | `DevExpress.AIIntegration.Blazor.Chat`, `DevExpress.Blazor`, `DevExpress.Blazor.Themes.Fluent` `26.1.6` (`:12-14`) | none |

## Deployment

`src/Ballware.Llm.App/Dockerfile`: `aspnet:10.0` base, `sdk:10.0` build/publish stages, `USER app`,
`ENTRYPOINT ["dotnet", "Ballware.Llm.App.dll"]` (`:1, 14, 50-55`); build args `GITHUB_USERNAME`,
`GITHUB_PAT`, `DEVEXPRESS_NUGETFEED`, `DEVEXPRESS_LICENSE` with hard guards on the DevExpress ones
(`:3-8`); the DevExpress license is written to `$HOME/.config/DevExpress/` (`:43`). CI builds it as
`ghcr.io/ballware/ballware-llm-app:<semver>` plus `stable`/`latest`
(`.github/workflows/publish-packages.yml:126-140`).

> Port inconsistency: the Dockerfile declares `EXPOSE 5000` / `EXPOSE 5001` (`:11-12`) while the app
> listens on `3004` (`appsettings.json:5`). Publish the container with an explicit mapping to `3004`.

## Related pages

- [architecture.md](architecture.md) — shared wiring
- [rag-context-and-tools.md](rag-context-and-tools.md) — the context/inference stack the App uses
- [../api/signalr-chat-hub.md](../api/signalr-chat-hub.md) — the Service's interactive alternative
