# Plan: Session-Logik nach Ballware.Shared.Web.Session ausgliedern

Ziel-Repository: `/Users/frank/Documents/GitHub/ballware-shared-dotnet`

Hinweis: Die Datei ist im aktuellen Workspace gespeichert, weil die aktuelle Planungsberechtigung Plan-Dateien außerhalb des Workspace nicht zulässt. Der Plan selbst beschreibt die Umsetzung für `ballware-shared-dotnet` und `ballware-document-service`.

## Kontext

Die Session-Logik liegt aktuell in `ballware-document-service` im Projekt `Ballware.Document.Session`:

- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/ServiceCollectionExtensions.cs`
  - `AddBallwareSession(...)`
  - `SessionAuthenticationSchemes.DistributedSession`
  - Registrierung des `DistributedSession`-Authentication-Schemas
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/WebApplicationExtensions.cs`
  - `UseBallwareSession(...)`
  - `app.UseSession()`
  - `CachedClaimsPrincipalMiddleware`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/OpenIdConnectOptionsExtensions.cs`
  - `RegisterBallwareSessionTokenHandling(...)`
  - Speichert `ClaimsPrincipal` nach `OnTokenValidated`
  - optional `SuppressDefaultSessionCookie`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/Configuration/SessionOptions.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/Internal/SessionPrincipalProvider.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/Internal/CachedClaimsPrincipalMiddleware.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Session/Internal/SessionPrincipalAuthenticationHandler.cs`

Verwendet wird die Logik insbesondere von:

- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Hosting/ServiceCollectionExtensions.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Hosting/BallwareDocumentOptions.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.Hosting/SignOnEndpointExtensions.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.App/Startup.cs`
- `/Users/frank/Documents/GitHub/ballware-document-service/src/Ballware.Document.App/Middleware/ChallengeDifferentiationMiddleware.cs`

Cross-Repo-Beziehungen:

- `ballware-document-service` konsumiert `ballware-shared-dotnet` über veröffentlichte NuGet-Pakete, z. B. `ballware.shared.authorization` in Version `1.1.0`.
- `ballware-shared-dotnet` packt/published alle Pakete über `ballware-shared.sln`, `GitVersion.yml` und `.github/workflows/publish-packages.yml`.
- Es gibt in `ballware-document-service` keine `NuGet.config`; lokale Cross-Repo-Validierung braucht daher eine temporäre lokale Quelle oder einen bereits veröffentlichten Paketstand.

## Ziel

Erstelle ein wiederverwendbares shared Paket `Ballware.Shared.Web.Session`, das die browserseitige Session-Principal-Logik enthält, sodass `ballware-document-service` und后续 `ballware-llm-service` nicht mehr eigene Session-Klassen kopieren müssen.

## Out of Scope

- Keine allgemeine OIDC-Bibliothek.
- Kein Verschieben des `/signon/{idToken}`-Endpoints in `Ballware.Document.Hosting` in diese Phase.
- Kein Verschieben der `ChallengeDifferentiationMiddleware` mit Document-spezifischen Transportpfaden wie `/_blazor`, `/DXX`, `/dxDR`.
- Kein Refactoring der OIDC-Challenge-Fehlerbehandlung in `SignInChallengeFallbackMiddleware`.
- Kein LLM-App-Aufbau.
- Keine persistenten DB-basierten Sessions.
- Keine Logout-/Session-Revocation-Logik.

## Assumptions

Diese Annahmen sind Teil des Plans, sofern nicht widersprochen wird:

1. `Ballware.Document.Session` wird intern nach dem Umzug entfernt, nicht als Compatibility-Shim belassen.
2. `RegisterBallwareSessionTokenHandling(...)` wird zunächst mit in das Session-Paket aufgenommen, damit die Session-Behandlung nach OIDC-Token-Validierung nicht fragmentiert.
3. `/signon/{idToken}` bleibt vorerst Document-spezifisch, weil Redirect-Allowlists und App-Pfade (`/viewer`, `/designer`) nicht allgemein sind.
4. Datenstruktur und Verhalten bleiben für `ballware-document-service` funktional gleich.

## Zielarchitektur

### Neues shared Paket

Projekt in `ballware-shared-dotnet`:

- `/Users/frank/Documents/GitHub/ballware-shared-dotnet/src/Ballware.Shared.Web.Session/Ballware.Shared.Web.Session.csproj`

Testprojekt:

- `/Users/frank/Documents/GitHub/ballware-shared-dotnet/test/Ballware.Shared.Web.Session.Tests/Ballware.Shared.Web.Session.Tests.csproj`

Namespace-Struktur:

- `Ballware.Shared.Web.Session`
- `Ballware.Shared.Web.Session.Configuration`
- `Ballware.Shared.Web.Session.Internal`

Dateien:

- `Configuration/WebSessionOptions.cs`
- `ISessionPrincipalProvider.cs`
- `SessionAuthenticationSchemes.cs`
- `ServiceCollectionExtensions.cs`
- `WebApplicationExtensions.cs`
- `OpenIdConnectSessionOptionsExtensions.cs`
- `Internal/SessionPrincipalProvider.cs`
- `Internal/CachedClaimsPrincipalMiddleware.cs`
- `Internal/SessionPrincipalAuthenticationHandler.cs`

## Öffentliche API

Empfohlene API:

```csharp
namespace Ballware.Shared.Web.Session;

public static class SessionAuthenticationSchemes
{
    public const string DistributedSession = "DistributedSession";
}

public interface ISessionPrincipalProvider
{
    Task<ClaimsPrincipal?> GetPrincipalAsync(string sessionId);
    Task StorePrincipalAsync(HttpContext context, ClaimsPrincipal principal);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareWebSession(
        this IServiceCollection services,
        WebSessionOptions options);
}

public static class WebApplicationExtensions
{
    public static WebApplication UseBallwareWebSession(this WebApplication app);
}

public static class OpenIdConnectSessionOptionsExtensions
{
    public static void RegisterBallwareWebSessionTokenHandling(
        this OpenIdConnectOptions options);
}
```

`WebSessionOptions`:

```csharp
namespace Ballware.Shared.Web.Session.Configuration;

public class WebSessionOptions
{
    public bool EnableSession { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string SessionCookieName { get; set; } = ".BallwareSession";

    [Required(AllowEmptyStrings = false)]
    public string SessionCacheKeyPrefix { get; set; } = "ballware:session:";

    [Range(1, int.MaxValue)]
    public int SessionCacheExpirationInMinutes { get; set; } = 60;

    public bool SuppressDefaultSessionCookie { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string DataProtectionPurpose { get; set; } = "Ballware.Web.Session.ClaimsPrincipal";
}
```

## Verhalten des Shared-Pakets

### `AddBallwareWebSession`

Erforderliche Registrierung:

1. `services.AddSingleton(options);`
2. `services.AddDataProtection();`
3. `services.AddSingleton<ISessionPrincipalProvider, SessionPrincipalProvider>();`
4. `services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SessionPrincipalAuthenticationHandler>(SessionAuthenticationSchemes.DistributedSession, null);`
5. Nur wenn `options.EnableSession` true ist: `services.AddSession();`

Wichtig:

- `AddBallwareWebSession` soll nicht `AddDistributedMemoryCache()` erzwingen. Der Host entscheidet über Memory-, Redis- oder andere `IDistributedCache`-Implementierung.
- `ballware-document-service` bleibt verantwortlich für `services.AddDistributedMemoryCache()` oder eine produktive `IDistributedCache`-Registrierung.
- `IDataProtectionProvider` wird durch das Paket sichergestellt; Document kann `ApplicationName` und `PersistKeysToFileSystem` weiterhin selbst konfigurieren.

### `UseBallwareWebSession`

Empfohlene Implementierung:

```csharp
var options =
    app.Services.GetService<WebSessionOptions>()
    ?? app.Services.GetRequiredService<IOptions<WebSessionOptions>>().Value;

if (options.EnableSession)
{
    app.UseSession();
}

app.UseMiddleware<CachedClaimsPrincipalMiddleware>();
return app;
```

Middleware-Reihenfolge im Host bleibt wie aktuell:

- `app.UseRouting();`
- `app.UseBallwareWebSession();`
- `app.UseMiddleware<ChallengeDifferentiationMiddleware>();`
- `app.UseAuthentication();`

### `SessionPrincipalProvider`

Aus aktuellem Code übernehmen, aber:

- DataProtection-Protector nicht mehr hart auf `Ballware.Document.Session.ClaimsPrincipal` fixieren.
- Stattdessen `options.DataProtectionPurpose` verwenden.
- Cookie-Name und Cache-Prefix bleiben konfigurierbar.
- `TicketSerializer.Default` bleibt unverändert.
- `AuthenticationTicket` bleibt Scheme `"DistributedSession"`.
- Cookie-Optionen zunächst unverändert lassen:
  - `HttpOnly = true`
  - `Secure = true`
  - `SameSite = SameSiteMode.None`
  - `Path = "/"`

### `OpenIdConnectSessionOptionsExtensions.RegisterBallwareWebSessionTokenHandling`

Aus aktuellem `Ballware.Document.Session.OpenIdConnectOptionsExtensions` übernehmen:

- `SaveTokens = false`
- `GetClaimsFromUserInfoEndpoint = false`
- `OnTokenValidated`:
  - `WebSessionOptions` und `ISessionPrincipalProvider` auflösen
  - Principal speichern
  - Wenn `SuppressDefaultSessionCookie` true ist:
    - relative `context.Properties?.RedirectUri` oder `/`
    - `context.HandleResponse()`
    - Redirect ausführen

Kein Verhalten für Document ändern.

## Document-Migration

### Package-Referenz

In `Ballware.Document.Hosting.csproj` und `Ballware.Document.App.csproj`:

- Entferne:

```xml
<ProjectReference Include="..\Ballware.Document.Session\Ballware.Document.Session.csproj" />
```

- Ergänze eine shared Package-Referenz:

```xml
<PackageReference Include="ballware.shared.web.session" Version="<next GitVersion SemVer>" />
```

Empfehlung:

- Wenn möglich, bestehende `ballware.shared.*`-Referenzen in Document auf dieselbe neue Version heben, also z. B. `1.2.0` oder die exakte GitVersion-SemVer aus der develop-Veröffentlichung.

### Importe und Aufrufe

In `Ballware.Document.Hosting/ServiceCollectionExtensions.cs`:

- `using Ballware.Document.Session;`
- `using Ballware.Document.Session.Configuration;`

ersetzen durch:

- `using Ballware.Shared.Web.Session;`
- `using Ballware.Shared.Web.Session.Configuration;`

Anpassungen:

- `SessionOptions` -> `WebSessionOptions`
- `services.AddBallwareSession(options.Session);`
  -> `services.AddBallwareWebSession(options.Session);`
- `openIdConnectOptions.RegisterBallwareSessionTokenHandling();`
  -> `openIdConnectOptions.RegisterBallwareWebSessionTokenHandling();`

In `BallwareDocumentOptions.cs`:

- `SessionOptions Session` -> `WebSessionOptions Session`

In `Ballware.Document.App/Startup.cs`:

- `using Ballware.Shared.Web.Session;`
- `app.UseBallwareWebSession();`

In `Ballware.Document.App/Middleware/ChallengeDifferentiationMiddleware.cs`:

- `using Ballware.Shared.Web.Session;`
- `SessionAuthenticationSchemes.DistributedSession` bleibt inhaltlich identisch.

In `Ballware.Document.Hosting/SignOnEndpointExtensions.cs`:

- `using Ballware.Shared.Web.Session;`
- `ISessionPrincipalProvider` stammt aus shared Paket.
- `/signon/{idToken}` bleibt im Document-Hosting.

### Konfiguration

Da shared Defaults neutral sind, muss `ballware-document-service` explizit die bisherigen Document-Werte setzen, damit bestehende Sessions, Cookies und Cache-Keys nicht brechen.

In `src/Ballware.Document.App/appsettings.json` den `Session`-Abschnitt vollständig machen:

```json
"Session": {
  "EnableSession": true,
  "SessionCookieName": ".BallwareDocumentSession",
  "SessionCacheKeyPrefix": "session:",
  "SessionCacheExpirationInMinutes": 60,
  "SuppressDefaultSessionCookie": true,
  "DataProtectionPurpose": "Ballware.Document.Session.ClaimsPrincipal"
}
```

Auch in produktiven Konfigurationsdateien oder Deployment-Settings prüfen, falls dort `Session` überschrieben wird.

## Projektstruktur-Bereinigung

Nach erfolgreicher Document-Migration:

1. `Ballware.Document.Session` aus `ballware-document-service.sln` entfernen:

```bash
dotnet sln ballware-document-service.sln remove src/Ballware.Document.Session/Ballware.Document.Session.csproj
```

2. Verzeichnis `src/Ballware.Document.Session` löschen.

3. Prüfen, dass keine `using Ballware.Document.Session;` mehr existieren.

## Shared-Repo-Aufgaben

1. Neues Projekt `src/Ballware.Shared.Web.Session` anlegen.
2. `TargetFramework net10.0`, `ImplicitUsings`, `Nullable` analog zu bestehenden shared Projekten.
3. `InternalsVisibleTo` für `Ballware.Shared.Web.Session.Tests` ergänzen.
4. FrameworkReference `Microsoft.AspNetCore.App` ergänzen.
5. `Microsoft.AspNetCore.Authentication.OpenIdConnect` mit passender Version ergänzen, bevorzugt passend zu Document-ASP.NET-Core-Version `10.0.5`.
6. Unused `Newtonsoft.Json` aus altem `Ballware.Document.Session.csproj` nicht übernehmen, sofern keine Quelldatei es verwendet.
7. Session-Logik mit neuen Namespaces und neutralen Namen übernehmen.
8. `WebSessionOptions` mit konfigurierbarer `DataProtectionPurpose` ergänzen.
9. Neues Testprojekt `test/Ballware.Shared.Web.Session.Tests` anlegen.
10. Tests ergänzen.
11. Projekte in `ballware-shared.sln` aufnehmen:

```bash
dotnet sln ballware-shared.sln add src/Ballware.Shared.Web.Session/Ballware.Shared.Web.Session.csproj
dotnet sln ballware-shared.sln add test/Ballware.Shared.Web.Session.Tests/Ballware.Shared.Web.Session.Tests.csproj
```

## Tests

### Shared-Paket-Tests

Mindestens:

- `AddBallwareWebSession`
  - registriert `WebSessionOptions`
  - registriert `ISessionPrincipalProvider`
  - registriert `DistributedSession`-Scheme
  - registriert Session nur bei `EnableSession = true`

- `SessionPrincipalProvider`
  - `StorePrincipalAsync` schreibt protected Ticket in `IDistributedCache`
  - gesetzter Session-Cookie mit Name, Path, HttpOnly, SameSite
  - `GetPrincipalAsync` liest Ticket und gibt `ClaimsPrincipal` zurück
  - unbekannter/abgelaufener Cache-Key liefert `null`

- `CachedClaimsPrincipalMiddleware`
  - wenn `HttpContext.User` unauthentifiziert und Cookie vorhanden: Principal wird gesetzt
  - wenn `HttpContext.User` bereits authentifiziert ist: keine Überschreibung

- `WebApplicationExtensions` optional
  - `UseBallwareWebSession` registriert Middleware korrekt und nutzt `WebSessionOptions`

### Nicht blockierend, aber empfohlen

- Integrationstest mit `WebApplicationFactory` oder minimalem Web-Host für:
  - `/signon`-ähnlichen principal store
  - OIDC `OnTokenValidated`-Handling

## Validierung

### Shared-Repo

```bash
dotnet restore ballware-shared.sln
dotnet test test/Ballware.Shared.Web.Session.Tests/Ballware.Shared.Web.Session.Tests.csproj
dotnet pack src/Ballware.Shared.Web.Session/Ballware.Shared.Web.Session.csproj --configuration Release
```

Optional lokal mit expliziter Version:

```bash
dotnet pack src/Ballware.Shared.Web.Session/Ballware.Shared.Web.Session.csproj -p:Version=1.2.0 --configuration Release --output ./nupkgs
```

### Document-Repo

Nach verfügbarem shared Paketstand:

```bash
dotnet restore ballware-document-service.sln
dotnet build ballware-document-service.sln
```

Wenn Testausführung in der Document-Repo ohne externe Abhängigkeiten sicher ist:

```bash
dotnet test test/Ballware.Document.Api.Tests/Ballware.Document.Api.Tests.csproj
```

### Manuelle Document-UAT

1. Document App mit bestehender `Session`-Konfiguration starten.
2. OIDC-Login ausführen.
3. Session-Cookie `.BallwareDocumentSession` vorhanden.
4. `/viewer` oder `/designer` authenticated erreichbar.
5. Transportpfad wie `/_blazor` ohne Authentifizierung liefert weiterhin `401`, nicht `302`.
6. `/signon/{idToken}?redirect=/viewer` setzt Session und redirectet sicher.
7. Nach Session-Verlust ist erneuter Login über `/signon` möglich.

## Risiken und Gegenmaßnahmen

### 1. Cookie-Namen- und Cache-Prefix-Drift

Shared defaults sind neutral. Ohne Document-Overrides entstehen neue Cookies/Keys und Sessions werden invalidiert.

Gegenmaßnahme:

- `Session`-Abschnitt in Document-Konfiguration vollständig auf alte Document-Werte setzen.

### 2. DataProtection-Purpose-Drift

Alte Tickets wurden mit `Ballware.Document.Session.ClaimsPrincipal` geschützt.

Gegenmaßnahme:

- Document setzt `DataProtectionPurpose` explizit auf `Ballware.Document.Session.ClaimsPrincipal`.
- Später kann eine eigene Migration auf neutrale Purpose geplant werden.

### 3. Cross-Repo-Paketversion

Document kann nicht project-referenziert auf shared Repo bauen.

Gegenmaßnahme:

- Shared zuerst bereitstellen.
- Document PR nur gegen veröffentlichte Version oder temporäre lokale Paketquelle bauen.

### 4. OIDC-Session-Handling wird in shared Paket gekoppelt

`RegisterBallwareWebSessionTokenHandling` benötigt `OpenIdConnectOptions`.

Gegenmaßnahme:

- Aktuell bewusst in Session-Paket belassen.
- Später in separates `Ballware.Shared.Web.Oidc` auslagern, wenn OIDC-Common-Code klar getrennt werden soll.

### 5. `/signon`-Endpoint bleibt Document-spezifisch

Der Endpoint enthält Redirect-Allowlists und Document-Pfade.

Gegenmaßnahme:

- Nicht mit ausgliedern.
- Nur `ISessionPrincipalProvider` wird gemeinsam genutzt.

## Empfohlene Reihenfolge

1. Shared-Paket implementieren und testen.
2. Shared-Paket lokal packen oder CI-Veröffentlichung auf `develop`/`main` auslösen.
3. Document auf shared Paket umstellen.
4. Document-Konfiguration explizit auf alte Session-Werte setzen.
5. Document bauen und manuelle Auth-Checks durchführen.
6. `Ballware.Document.Session` löschen und Document-Solution bereinigen.

## Offene Punkte, aber empfohlene Antworten

1. Soll `Ballware.Document.Session` als Compatibility-Shim bleiben?
   - Empfohlen: Nein. Es sind nur interne Document-Projekte betroffen.

2. Soll OIDC-`OnTokenValidated`-Handling sofort mit umziehen?
   - Empfohlen: Ja, in `Ballware.Shared.Web.Session`, um Funktionsbruch zu vermeiden.

3. Soll `/signon/{idToken}` mit umziehen?
   - Empfohlen: Nein, vorerst in `Ballware.Document.Hosting` belassen.

4. Welche Paketversion soll Document referenzieren?
   - Empfohlen: exakte GitVersion-SemVer des neuen shared Releases; nicht hart codiert im shared csproj.
