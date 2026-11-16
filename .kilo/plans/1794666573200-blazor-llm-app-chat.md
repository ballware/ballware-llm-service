# Ballware LLM Chat App — Implementierungsplan

## Ziel
Ein neuer Blazor-Server-Frontend-Host `Ballware.Llm.App` parallel zu `Ballware.Llm.Service`, mit:
- Chat unter `/chat/{contextIdentifier}`
- DevExpress `DxAIChat`
- echtem inkrementellem Streaming
- Context-Validierung mit klarer Fehlermeldung
- iframe-Login über `/signon/{idToken}`
- standalone OIDC-Redirect-Login
- gemeinsamer LLM-Komposition analog zu `Ballware.Document.Hosting`
- gemeinsamer Session-Mechanik über `Ballware.Shared.Web.Session` (in `ballware-shared-dotnet` umgesetzt und gepusht)
- Browser-OIDC-/SignOn-Mechanik über `Ballware.Shared.Web.Oidc` (in `ballware-shared-dotnet` umgesetzt)

## Wichtige Erkenntnisse
- `ballware-llm-service` enthält derzeit kein Blazor-, Razor-, wwwroot- oder SPA-Frontend.
- `Ballware.Llm.Service` ist der einzige `OutputType=Exe`-Host.
- `Context.Identifier` ist ein tenant-scoped `string` und wird im HTTP-Chat als `model` übergeben:
  - `POST /ai/v1/chat/completions` liest `body.Model` als `contextIdentifier`.
  - `ChatHub` hardcodiert `"global"`.
- `IChatSession` hat aktuell nur `Task PromptAsync(string message, Action<string> tokenHandler)`.
- `AiChatSession` streamt zwar intern, puffert aber die komplette Antwort und ruft `tokenHandler` einmal auf.
- `OpenAiChatCompletionRequest` enthält keine `conversation_id`, `user`, `session_id`, `max_tokens` oder Tools.
- `IChatResponseProvider.GetResponseAsync(IEnumerable<ChatMessage>, bool, string, CancellationToken)` kann für `DxAIChat` custom implementiert werden.
- `DxAIChat.ChatResponseProviderServiceKey` oder ein default registrierter `IChatResponseProvider` sind die richtige Integration für custom providers.
- `DxAIChat`-Streaming wird über `UseStreaming=true` aktiviert.
- Document.App nutzt Cookie + OIDC + JWT + `DistributedSession` + `/signon/{idToken}` + `ChallengeDifferentiationMiddleware` + `SignInChallengeFallbackMiddleware`.
- `Ballware.Shared.Web.Session` ist in `ballware-shared-dotnet` umgesetzt und auf `develop` gepusht (Commit `30a3bb8`): `AddBallwareWebSession`, `UseBallwareWebSession`, `ISessionPrincipalProvider`, `SessionAuthenticationSchemes.DistributedSession`, `OpenIdConnectSessionOptionsExtensions.RegisterBallwareWebSessionTokenHandling`, interne `SessionPrincipalProvider`/`CachedClaimsPrincipalMiddleware`/`SessionPrincipalAuthenticationHandler`; 12 Tests grün.
- Das Package registriert `AddDataProtection()` intern, aber keinen `IDistributedCache` — der Verbraucher muss z. B. `AddDistributedMemoryCache()` selbst registrieren.
- `Ballware.Shared.Web.Oidc` ist umgesetzt (40 Tests grün): `BrowserAuthorizationOptions`, `AddBallwareBrowserAuthorization` (Configuration- und Options-Überladung, registriert intern `AddBallwareWebSession`), `OpenIdConnectPublicRedirectExtensions` (`RegisterBallwarePublicRedirectUriHandling`, `NormalizePublicBaseUrl`), `SignOnEndpointExtensions.MapBallwareSignOnEndpoint`, `SignInChallengeFallbackMiddleware` + `UseBallwareSignInChallengeFallback()`, konfigurierbare Policies (`UserScopePolicyName` fällt auf `RequiredUserScope` zurück, `ServiceScopePolicyName` optional).
- `ChallengeDifferentiationMiddleware` ist jetzt im Session-Package mit konfigurierbaren Transportpfadpräfixen (`UseBallwareChallengeDifferentiation()`, Default `/_blazor`, `/DXX`, `/dxDR`); `SignInChallengeFallbackMiddleware` im Oidc-Package — beide damit nicht mehr Document.App-lokal nötig.
- Versionierung über GitVersion 6.8.2: lokaler Pack `1.3.0-alpha.1`; der gepushte Commit publiziert via CI `1.3.0-alpha.2`+ auf GitHub Packages.
- `Ballware.Document.App`/`Ballware.Document.Hosting` referenzieren bereits `Ballware.Shared.Web.Session`; `Ballware.Document.Session` wurde entfernt.

## Verbindliche Architektur-Entscheidungen
1. `Ballware.Llm.App` referenziert nicht direkt `Ballware.Llm.Service`.
2. Gemeinsame LLM-Komposition wird in `Ballware.Llm.Hosting` extrahiert.
3. Session-Mechanik wird über `Ballware.Shared.Web.Session` in `ballware-shared-dotnet` bereitgestellt (umgesetzt); `ballware-document-service` wurde bereits darauf migriert. OIDC-/SignOn-Mechanik wird über `Ballware.Shared.Web.Oidc` bereitgestellt (umgesetzt); die Document-Migration auf dieses Package ist als Out-of-Scope zurückgestellt.
4. `Ballware.Llm.App` nutzt dieselben LLM-Daten-/Inference-Dienste wie der Service, aber ohne Quartz-Jobs und ohne `Llm:AutoMigrations`.
5. Chat-Route ist `/chat/{contextIdentifier}`.
6. Unbekannter oder fehlender Context führt im App-Chat zu einer klaren Fehlerseite, nicht zu stiller `"global"`-Fallback-Komposition.
7. Streaming muss wirklich inkrementell sein, nicht nominal nach vollständiger Antwort.
8. `IChatSession` wird um conversation history erweitert. Persistente DB-History ist außerhalb dieses ersten Schritts.
9. Chat-Anhang-/File-Upload ist außerhalb dieses ersten Schritts.
10. Bestehende Service-API-Auth (`JWT` nur) bleibt für `Ballware.Llm.Service` funktional erhalten.

## Vorgesehene neue Packages
### In `ballware-shared-dotnet`
- `Ballware.Shared.Web.Session` — umgesetzt und gepusht (Commit `30a3bb8`)
  - `WebSessionOptions` (`EnableSession`, `SessionCookieName`, `SessionCacheKeyPrefix`, `SessionCacheExpirationInMinutes`, `SuppressDefaultSessionCookie`, `DataProtectionPurpose`)
  - `SessionAuthenticationSchemes.DistributedSession`
  - `ISessionPrincipalProvider`
  - `SessionPrincipalProvider` (internal)
  - `CachedClaimsPrincipalMiddleware` (internal)
  - `AddBallwareWebSession` / `UseBallwareWebSession`
  - `OpenIdConnectSessionOptionsExtensions.RegisterBallwareWebSessionTokenHandling`
  - keine `ChallengeDifferentiationMiddleware`/`SignInChallengeFallbackMiddleware` — die bleiben App-lokal
- `Ballware.Shared.Web.Oidc` — umgesetzt (Commit ausstehend, lokal gepackt als `1.3.0-alpha.2`)
  - `BrowserAuthorizationOptions` (`Authority`, `Audience`, `ClientId`, `Issuer`, `RequireHttpsMetadata`, `RequiredUserScope`, `UserScopePolicyName`, `ServiceScopePolicyName`, `SignOnAllowedHosts`, `SignOnAllowedRedirectPathPrefixes`, `PublicBaseUrl`)
  - `AddBallwareBrowserAuthorization` — Cookie + OIDC + JwtBearer + Policies + `ConfigurationManager<OpenIdConnectConfiguration>` + `AddBallwareWebSession`
  - `OpenIdConnectPublicRedirectExtensions` mit `PublicBaseUrl`-basierte `redirect_uri`-Ableitung
  - `MapBallwareSignOnEndpoint` (`/signon/{idToken}`) mit `SignOnAllowedHosts` und `SignOnAllowedRedirectPathPrefixes`
  - `SignInChallengeFallbackMiddleware` + `UseBallwareSignInChallengeFallback()`

### In `ballware-llm-service`
- `Ballware.Llm.Hosting`
- `Ballware.Llm.App`

## Task-Reihenfolge

### 1. Shared Session in `ballware-shared-dotnet` (abgeschlossen)
Umgesetzt und auf `develop` gepusht (Commit `30a3bb8`):
1. Neue Projekte `src/Ballware.Shared.Web.Session` und `test/Ballware.Shared.Web.Session.Tests` (NUnit, 12 Tests grün), in `ballware-shared.sln` aufgenommen.
2. `AddBallwareWebSession` registriert `AddDataProtection()`, `ISessionPrincipalProvider`, das Scheme `SessionAuthenticationSchemes.DistributedSession` und `AddSession()` nur bei `EnableSession`.
3. `UseBallwareWebSession` schaltet `CachedClaimsPrincipalMiddleware` scharf.
4. `RegisterBallwareWebSessionTokenHandling` speichert den OIDC-Principal im Session und unterdrückt das Standard-Session-Cookie (`SuppressDefaultSessionCookie`).
5. Version über GitVersion: lokaler Pack `1.3.0-alpha.1`; CI publiziert nach dem Push `1.3.0-alpha.2`+ auf GitHub Packages. `ballware-document-service` wurde bereits auf das Package migriert (`Ballware.Document.Session` entfernt).

### 1b. `Ballware.Shared.Web.Oidc` (abgeschlossen, Commit ausstehend)
Umgesetzt in `src/Ballware.Shared.Web.Oidc` + `test/Ballware.Shared.Web.Oidc.Tests` (40 Tests grün), lokal gepackt als `1.3.0-alpha.2`:
1. Muster aus `Ballware.Document.Hosting` überführt: `ServiceCollectionExtensions` (OIDC-Registrierungsteil), `SignOnEndpointExtensions`, `OpenIdConnectPublicRedirectExtensions`.
2. `BrowserAuthorizationOptions` ohne Document-spezifische Policies; `UserScopePolicyName` fällt auf `RequiredUserScope` zurück (LLM: `"mlApi"`), `ServiceScopePolicyName` nur bei Setzen registriert (z. B. `"serviceApi"`).
3. `MapBallwareSignOnEndpoint`: `SignOnAllowedHosts` + `SignOnAllowedRedirectPathPrefixes` (Segmentpräfixe; exakte Wurzel `"/"` immer erlaubt — für LLM also z. B. `["/chat"]`).
4. `ChallengeDifferentiationMiddleware` (Session-Package, `UseBallwareChallengeDifferentiation()` mit konfigurierbaren Präfixen) und `SignInChallengeFallbackMiddleware` (Oidc-Package, `UseBallwareSignInChallengeFallback()`) sind jetzt shared; der lokale Fallback ist nicht nötig.
5. Hinweis Semantik: `SignOnAllowedRedirectPathPrefixes` sind `PathString`-Segmentpräfixe — `"/"` als Präfix erlaubt keine Beliebige-Pfade, die Wurzel ist separat immer erlaubt.

Validierung:
```bash
dotnet build ballware-shared.sln
dotnet test test/Ballware.Shared.Web.Session.Tests/Ballware.Shared.Web.Session.Tests.csproj
dotnet test test/Ballware.Shared.Web.Oidc.Tests/Ballware.Shared.Web.Oidc.Tests.csproj
```
workdir: `/Users/frank/Documents/GitHub/ballware-shared-dotnet`

### 2. `Ballware.Llm.Hosting` anlegen
1. Neues Projekt `src/Ballware.Llm.Hosting/Ballware.Llm.Hosting.csproj` (`net10.0`) erstellen.
2. Projektreferenzen aufnehmen:
   - `Ballware.Llm.Api`
   - `Ballware.Llm.Context`
   - `Ballware.Llm.Data`
   - `Ballware.Llm.Data.Ef`
   - `Ballware.Llm.Data.Ef.Postgres`
   - `Ballware.Llm.Embedding`
   - `Ballware.Llm.Embedding.OpenAi`
   - `Ballware.Llm.Embedding.SemanticKernel`
   - `Ballware.Llm.Inference`
   - `Ballware.Llm.Inference.OpenAi`
   - `Ballware.Llm.Metadata`
   - `Ballware.Llm.Tools`
   - `Ballware.Llm.Tools.Common`
   - `Ballware.Llm.Tools.Embedding`
3. Optional `Ballware.Llm.Ingestion.Docling` nur dann referenzieren, wenn Upload-/Ingestion-Komposition mitaktiviert werden soll. Für den ersten Text-Chat-Step nicht erforderlich.
4. Gemeinsame Konfigurationsoptionen aus `Ballware.Llm.Service` übernehmen, aber nicht zwingend Swagger/Jobs.
5. Extensions mit klaren Zuständigkeiten implementieren:
   - `AddBallwareLlmCoreConfiguration`
   - `AddBallwareLlmClients`
   - `AddBallwareLlmDataStore`
   - `AddBallwareLlmContextAndInference`
   - `AddBallwareLlmTools`
   - `AddBallwareLlmMetadataAndStorageAdapters`
   - `AddBallwareLlmSharedAuthorizationUtils`
6. `Ballware.Llm.Service/Startup.cs` auf diese Extensions umstellen, ohne bestehende Endpunkte oder JWT-Auth zu verändern.
7. `Ballware.Llm.Service` darf weiterhin `Ballware.Llm.Hosting` referenzieren.

Validierung:
```bash
dotnet build ballware-llm-service.sln
```
workdir: `/Users/frank/Documents/GitHub/ballware-llm-service`

### 3. `IChatSession` um History und echtes Streaming erweitern
1. In `Ballware.Llm.Inference` neue eigene DTOs ohne DevExpress-/Microsoft.Extensions.AI-Abhängigkeit einführen:
   - `ChatRole`
   - `ChatMessage`
   - `ChatHistoryItem` oder direkt `ChatMessage`
2. `IChatSession` additive erweitern:
```csharp
public interface IChatSession
{
    IReadOnlyList<ChatMessage> History { get; }

    IAsyncEnumerable<string> PromptStreamingAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task PromptAsync(
        string message,
        Action<string> tokenHandler,
        CancellationToken cancellationToken = default);
}
```
3. `ChatContextOptions` erweitern um optionale `InitialHistory`, falls `DxAIChat` bereits geladene Nachrichten mitschickt:
```csharp
public class ChatContextOptions
{
    public string? Model { get; set; }
    public int? NumCtx { get; set; }
    public string? SystemPrompt { get; set; }
    public IReadOnlyList<ChatMessage>? InitialHistory { get; set; }
}
```
4. `AiChatSession` intern auf `Microsoft.Extensions.AI.ChatMessage`-Liste umstellen und `GetStreamingResponseAsync(messages, options)` verwenden.
5. `PromptStreamingAsync` muss Textdeltas sofort yielden.
6. `PromptAsync` kann compatibility-Wrapper sein und pro Delta `tokenHandler` aufrufen.
7. Assistant-History erst nach erfolgreichem Abschluss appenden.

### 4. Bestehende Chat-Pipeline auf echtes Streaming umstellen
1. `ChatEndpoint.HandleOpenAiChatCompletion` ändern:
   - für `body.Stream == true` `chat.PromptStreamingAsync(prompt)` verwenden
   - SSE-Chunks pro Textdelta schreiben
   - finale `finish_reason`-Chunk und `data: [DONE]` behalten
2. `ChatHub.Prompt` weiterhin über `PromptAsync` laufen lassen; dadurch werden Token-Deltas künftig direkt pro Delta an `Response` gesendet.
3. Bestehende non-streaming Antwort bleibt ein finaler `OpenAiChatCompletionResponse`.
4. Die bisherige "erst alles sammeln, dann einen Chunk schreiben"-Logik entfernen.

### 5. `Ballware.Llm.App` anlegen
1. Neues Projekt `src/Ballware.Llm.App/Ballware.Llm.App.csproj` (`net10.0`, `OutputType=Exe`, `IsPackable=false`) erstellen.
2. Projektreferenzen:
   - `Ballware.Llm.Hosting`
   - ggf. `Ballware.Llm.Context`
   - ggf. `Ballware.Llm.Inference`
   - ggf. `Ballware.Llm.Data`
3. NuGet-Pakete:
   - `DevExpress.Blazor` Version passend zu Document.App, bevorzugt `26.1.6`
   - `DevExpress.Blazor.Themes.Fluent` Version passend zu Document.App
   - `DevExpress.AIIntegration.Blazor.Chat` in kompatibler DevExpress-Version
   - `Serilog.AspNetCore` falls wie Service/App üblich gewünscht
   - `Ballware.Shared.Web.Session` und `Ballware.Shared.Web.Oidc` in den von CI publizierten Versionen (`>= 1.3.0-alpha.3` nach dem nächsten Shared-Commit; lokal: `nupkgs`-Feed)
4. Struktur analog `Ballware.Document.App`:
   - `Program.cs`
   - `Startup.cs`
   - `Components/App.razor`
   - `Components/Routes.razor`
   - `Components/_Imports.razor`
   - `Components/Layout/MainLayout.razor`
   - `Components/Pages/Chat.razor`
5. `Program.cs` wie `Ballware.Document.App/Program.cs`:
   - Configuration sources: `appsettings.json`, `appsettings.{Env}.json`, `appsettings.local.json`, env vars
   - Serilog
   - Kestrel binden
   - `new Startup(...)`
   - `startup.InitializeServices()`
   - `app.UseSerilogRequestLogging()`
   - `startup.InitializeApp(app)`
6. `Startup.cs`:
   - `AddRazorComponents().AddInteractiveServerComponents()`
   - `AddDevExpressBlazor()`
   - `AddDevExpressAI()`
   - `AddBallwareLlmCoreConfiguration`
   - `AddBallwareLlmClients`
   - `AddBallwareLlmDataStore`
   - `AddBallwareLlmContextAndInference`
   - `AddBallwareLlmTools`
   - `AddBallwareLlmMetadataAndStorageAdapters`
   - shared browser auth via `AddBallwareBrowserAuthorization` (Cookie + OIDC + JwtBearer + Policies + Session; `RegisterBallwareWebSessionTokenHandling` bereits intern)
   - `IDistributedCache` selbst registrieren (z. B. `AddDistributedMemoryCache()`)
   - antiforgery cookie `.Ballware.Antiforgery`
   - CSP `frame-ancestors` konfigurierbar über `ContentSecurity:FrameAncestors`
   - `MapBallwareSignOnEndpoint` (`SignOnAllowedRedirectPathPrefixes = ["/chat"]`, Wurzel `"/"` ist immer erlaubt)
   - `MapRazorComponents<App>().AddInteractiveServerRenderMode(...)`
7. Middleware-Reihenfolge analog Document.App:
   - exception handler
   - forwarded headers
   - routing
    - session principal middleware (`UseBallwareWebSession()` aus `Ballware.Shared.Web.Session`)
    - challenge differentiation middleware für `/_blazor` (`UseBallwareChallengeDifferentiation()`)
    - authentication
    - signon challenge fallback (`UseBallwareSignInChallengeFallback()`)
   - authorization
   - antiforgery
   - static files
   - culture `de-DE`
8. `Routes.razor`:
   - `CascadingAuthenticationState`
   - `AuthorizeRouteView`
   - `DefaultLayout=@typeof(MainLayout)`
   - deutschsprachige `NotAuthorized`- und `NotFound`-Meldungen

### 6. Chat-Page und Context-Validierung
1. `Chat.razor` mit `@page "/chat/{contextIdentifier?}"` und `[Authorize]`.
2. Tenant-ID und UserID aus `AuthenticationStateProvider` / `IPrincipalUtils` ableiten.
3. Vor dem Rendern des Chats `IContextRepository.GetByIdentifierAsync(tenantId, contextIdentifier)` laden.
4. Wenn `contextIdentifier` fehlt, leer oder Context nicht existiert:
   - keine `IInferenceEngine.StartChatAsync` aufrufen
   - klare Fehlerseite anzeigen, z. B. `Unbekannter oder fehlender Chat-Kontext.`
5. Wenn Context existiert:
   - `IModelContextProvider.ProvideWelcomeMessageAsync` laden
   - `IAIChat.LoadMessages` in `Initialized` mit Welcome-Message füllen
   - Chat-Context für den `DxAIChat`-Provider bereitstellen

### 7. `DxAIChat`-Backend-Adapter
1. Implementiere eine `IChatResponseProvider`-Implementierung in `Ballware.Llm.App` oder einem kleinen `Ballware.Llm.App.Chat`-Namespace.
2. Registrierung:
```csharp
services.AddScoped<BallwareChatResponseProvider>();
services.AddScoped<IChatResponseProvider>(sp => sp.GetRequiredService<BallwareChatResponseProvider>());
```
3. Provider muss `GetResponseAsync(IEnumerable<ChatMessage>, bool, string?, CancellationToken)` implementieren.
4. Provider verwendet `IChatSession` als authoritative conversation history.
5. Bei erstem Aufruf:
   - Context-Identifier aus scoped ChatContext holen
   - `ProvideOptionsAsync` und `ProvideSystemPromptAsync` aufrufen
   - `IInferenceEngine.StartChatAsync` aufrufen
   - initial user message als `initialPrompt` verwenden
6. Bei Folgeaufrufen:
   - nur die letzte User-Message an `IChatSession.PromptStreamingAsync` übergeben
   - History bleibt serverseitig in `IChatSession`
7. Für jede Textdelta einen `ChatResponseUpdate` mit `Role = ChatRole.Assistant` yielden.
8. `ConversationId` auf `ChatResponseUpdate` setzen, damit `DxAIChat` Folgeanfragen mit Conversations-Kontext stellt.
9. `DxAIChat` im Razor mit `UseStreaming="true"` konfigurieren.
10. Kein DB-persistenter Chat-Historie-Speicher in dieser Phase.

### 8. Konfiguration für `Ballware.Llm.App`
Benötigte Konfigurationsabschnitte:
- `Kestrel`
- `Serilog`
- `Session`
- `Authorization`
- optional `Cors`
- optional `Mcp`
- `Llm`
- `ConnectionStrings:LlmStorageConnection`
- `Inference`
- `Embedding`
- `MetaClient`
- `StorageClient`
- `ServiceCredentials`
- `ContentSecurity:FrameAncestors`

Wichtige Vorgaben:
- `Llm:AutoMigrations = false`
- kein `Quartz`-Block in App erforderlich
- `Authorization:ClientId` für OIDC und `/signon`-Validierung verwenden
- `Authorization:PublicBaseUrl` muss der öffentlichen Browser-Origin der App sein
- `/signin-oidc` muss beim IdP als gültige Redirect-URI für `ClientId` registriert sein
- iframe-Hosts müssen in `ContentSecurity:FrameAncestors` und `Authorization:SignOnAllowedHosts` erlaubt sein

Beispielstruktur ohne Secrets:
```json
{
  "ContentSecurity": {
    "FrameAncestors": "'self' https://reporting.ballware.de"
  },
  "Session": {
    "SessionCookieName": ".Ballware.Llm.Session",
    "SessionCacheKeyPrefix": "llm:session:",
    "SessionCacheExpirationInMinutes": 60,
    "DataProtectionPurpose": "Ballware.Llm.Session.ClaimsPrincipal"
  },
  "Authorization": {
    "Authority": "https://keycloak.example/realms/ballware",
    "Audience": "metaApi",
    "ClientId": "llm-app",
    "PublicBaseUrl": "https://llm.ballware.example",
    "SignOnAllowedHosts": ["llm.ballware.example"],
    "SignOnAllowedRedirectPathPrefixes": ["/chat"],
    "RequiredUserScope": "mlApi",
    "ServiceScopePolicyName": "serviceApi",
    "RequireHttpsMetadata": true
  },
  "Llm": {
    "Provider": "postgres",
    "AutoMigrations": false
  }
}
```

## Out of Scope
- Persistente Chat-Historie in Postgres
- File-Upload direkt in `DxAIChat`
- Docling-Ingestion aus dem Chat-Frontend
- Quartz-Jobs im App
- AutoMigrations im App
- Migration der restlichen Document-Mechaniken (OIDC/SignOn, Challenge-Middlewares) auf Shared-Packages — die Session-Migration ist bereits erfolgt
- Tenantable Editing APIs in `Ballware.Llm.App`
- Änderung der `ChatHub`-Context-Auswahl für bestehende SignalR-Clients

## Risken
- DevExpress-Paketversion: `DevExpress.Blazor 26.1.6` ist in Document.App belegt; `DevExpress.AIIntegration.Blazor.Chat` muss in kompatibler Version gewählt werden.
- `Ballware.Shared.Web.Oidc` und `Ballware.Shared.Web.Session` sind local fertig, aber für `ballware-llm-service` erst ab der CI-publizierten GitHub-Packages-Version (`>= 1.3.0-alpha.3` nach Commit/Push der Oidc-Änderungen) verfügbar; für lokale Builds vor CI hilft ein lokaler Feed (Ordner `nupkgs` in `ballware-shared-dotnet`).
- GitVersion erhöht die Version mit jedem Commit auf `develop` — `PackageReference`-Versionen müssen nach jedem Shared-Push nachgezogen werden.
- `DxAIChat` läuft über SignalR-Circuit `/_blazor`; fehlende Auth darf nicht als OIDC-302 auf Transport-Requests enden.
- `Authorization:PublicBaseUrl` und IdP-Redirect-URIs sind bei Proxy-/Container-Setups anfällig.
- `IChatSession`-Kontrakt ist eine breaking/additive Änderung; bestehende Aufrufer müssen angepasst werden.

## Test- und Validierungsplan
1. Shared Repo bauen und testen (Regression; Session und Oidc bereits validiert):
```bash
dotnet build ballware-shared.sln
dotnet test test/Ballware.Shared.Web.Session.Tests/Ballware.Shared.Web.Session.Tests.csproj
dotnet test test/Ballware.Shared.Web.Oidc.Tests/Ballware.Shared.Web.Oidc.Tests.csproj
```
workdir: `/Users/frank/Documents/GitHub/ballware-shared-dotnet`

2. LLM Service Solution bauen:
```bash
dotnet build ballware-llm-service.sln
```
workdir: `/Users/frank/Documents/GitHub/ballware-llm-service`

3. Vorhandene Tests ausführen:
```bash
dotnet test test/Ballware.Llm.Ingestion.Docling.Tests/Ballware.Llm.Ingestion.Docling.Tests.csproj
```
workdir: `/Users/frank/Documents/GitHub/ballware-llm-service`

4. Manuelle App-Checks:
   - `/` leitet ohne Session zu OIDC-Login oder zeigt geschützte Seite
   - `/signon/{idToken}?redirect=/chat/global` setzt Session-Cookie und leitet weiter
   - `/chat/global` rendert `DxAIChat`
   - `/chat/unknown` zeigt klare Fehlerseite
   - `/chat/` ohne Identifier zeigt klare Fehlerseite
   - Chat-Antwort wird tokenweise im UI aktualisiert
   - `/_blazor`-Circuit gibt bei fehlender Session `401`, nicht OIDC-302
   - iframe-Framing funktioniert nur mit erlaubten `frame-ancestors`

5. Service-Kompatibilitätschecks:
   - `POST /ai/v1/chat/completions` mit `stream=false` liefert finale Antwort
   - `POST /ai/v1/chat/completions` mit `stream=true` liefert mehrere `chat.completion.chunk`-Frames vor `[DONE]`
   - `ChatHub` sendet `Response`-Events pro Token-Delta
   - bestehende Swagger-/JWT-Konfiguration bricht nicht durch `Ballware.Llm.Hosting`

## Empfohlene Implementierungsreihenfolge
1. Shared-Auth/Session in `ballware-shared-dotnet` fertigstellen und paketieren — erledigt: Session (`30a3bb8`) und Oidc (arbeitskopie, Tests grün, lokal `1.3.0-alpha.2`); Commit/Push durch User ausstehend, danach CI-Publish `1.3.0-alpha.3`.
2. `Ballware.Llm.Hosting` extrahieren und Service ohne Verhaltensänderung refactorieren.
3. `IChatSession` streaming/history erweitern und bestehende Chat-Pipeline anpassen.
4. `Ballware.Llm.App` als Blazor-Host mit OIDC/Session/SignOn anlegen.
5. `DxAIChat`-Provider und Context-Validierung implementieren.
6. Konfiguration und manuelle Auth-/Chat-Checks ausführen.
