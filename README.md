# ballware LLM Service

Der **ballware LLM Service** ist ein plattformseitiger Microservice der ballware-Produktfamilie, der LLM-gestützte Chat-Funktionen mit **RAG (Retrieval Augmented Generation)** über eine tenantfähige Wissensbasis kombiniert.

Der Service konvertiert Dokumente (via **Docling Serve**), zerlegt sie in Text-Chunks, erzeugt Vektoren über eine **OpenAI-kompatible Embedding-API** und speichert sie in **PostgreSQL mit pgvector**. Zur Laufzeit wird aus tenant- und dokumentbezogenen Kontextinformationen ein System-Prompt gebaut, das zusammen mit registrierten Tools (eigene Knowledge-Tools, Math-Tools und externe **MCP**-Server) an einen OpenAI-kompatiblen Chat-Endpoint übergeben wird.

- Zielplattform: **.NET 10** (`net10.0`)
- Datenhaltung: **PostgreSQL / pgvector** (`vector(768)`), Entity Framework Core 10 + Npgsql
- Authentifizierung: **OIDC / JWT Bearer** (mandantenfähig, Scope-basierte Policies)
- Kommunikation: HTTP/Minimal-API (inkl. **OpenAI-kompatibler** `chat/completions`), **SignalR**-Chat-Hub
- Hintergrundverarbeitung: **Quartz.NET** (geclustert, PostgreSQL-JobStore)

---

## Architekturüberblick

Die Solution ist in schlanke, abhängigkeitsarme Projekte mit klaren Contracts und OpenAI-spezifischen Implementierungen getrennt (Ports & Adapters). Jeder Block wird über `ServiceCollectionExtensions`-Methoden im `Ballware.Llm.Service`-Startup zusammengesetzt.

| Projekt | Rolle |
|---|---|
| `Ballware.Llm.Service` | Composition Root: `Program.cs`, `Startup.cs`, Konfiguration, Auth, DI-Verdrahtung, Swagger, Docker-Entry-Point |
| `Ballware.Llm.Api` | HTTP-Endpoints (Minimal-API), OpenAI-kompatibler Chat, SignalR `ChatHub`, Sitzungsstore, Request-/Response-Modelle |
| `Ballware.Llm.Inference` | Contracts der Inferenz: `IInferenceEngine`, `IChatSession`, `ChatContextOptions` |
| `Ballware.Llm.Inference.OpenAi` | Implementierung auf Basis `OpenAI`-SDK + `Microsoft.Extensions.AI` (`ChatClient` → `IChatClient`, `UseFunctionInvocation()`), Tool-Registry, MCP-Anbindung |
| `Ballware.Llm.Embedding` | Contracts: `IEmbeddingEngine`, `IChunker<T>`, `ChunkerRegistry`, `ChunkingOptions` |
| `Ballware.Llm.Embedding.OpenAi` | Embedding-Engine über `EmbeddingClient` (`IEmbeddingGenerator<string, Embedding<float>>`) |
| `Ballware.Llm.Embedding.SemanticKernel` | `MarkdownChunker` auf Basis `Microsoft.SemanticKernel.Text.TextChunker` |
| `Ballware.Llm.Context` | System-Prompt-Komposition, Kontext-Recherche über globale Origins, Token-Schätzung (`SmartFormat.NET`) |
| `Ballware.Llm.Tools` | Tool-Contracts: `IToolRegistry`, `IToolProvider`, `Tool`, `ToolParameter`, `IMcpService` |
| `Ballware.Llm.Tools.Common` | `MathToolProvider` (`add`, `subtract`) |
| `Ballware.Llm.Tools.Embedding` | `EmbeddingsToolProvider` (`origins.list`, `origins.content`, `origins.query`) |
| `Ballware.Llm.Ingestion` | Contract `IKnowledgeIngestor`, `IngestionResult`, `IngestionImage` |
| `Ballware.Llm.Ingestion.Docling` | `DoclingMarkdownIngestor` / `DoclingJsonIngestor`, `DoclingHttpApiClient`, `DoclingImageExtractor` |
| `Ballware.Llm.Docling.Client` | NSwag-generierter Client für die Docling-Serve-OpenAPI (plus handgeschriebene Extension für Async-Konvertierung) |
| `Ballware.Llm.Data` | Domain-Modelle (`Persistables` für EF, `Public` für API) und Repository-Interfaces |
| `Ballware.Llm.Data.Ef` | EF-Repository-Basisklassen, Mapster-Mapping, `LlmStorageOptions`, Modell-Customizer |
| `Ballware.Llm.Data.Ef.Postgres` | `LlmDbContext`, Npgsql/pgvector-Konfiguration, Vektor-Repositories, `InitializationWorker`, Migrations |
| `Ballware.Llm.Jobs` | Quartz-Jobs: Origin-Import, Embedding-Erzeugung, Embedding-Neuerzeugung |
| `Ballware.Llm.Metadata` | `Tenant`, `EntityMetadata`, `Attachment` (Metadaten-/Rights-Check-Modelle) |
| `Ballware.Llm.Ingestion.Docling.Tests` | NUnit-Tests mit Testcontainers (WireMock) für die Docling-Ingestion |

Externe Plattformabhängigkeiten: `ballware.shared.*` (API-Endpoints, Authorization, Data, Jint-Rights-Check), `ballware.meta.service.client` (Metadaten/Jobs/Exports), `ballware.storage.service.client` (Datei-/Attachment-Storage), `Duende.AccessTokenManagement` (Client-Credentials-Flows), Serilog, Swashbuckle, Mapster.

---

## Funktionsumfang

### 1. Knowledge-Ingestion (Dokumente → Chunks → Vektoren)

1. Ein `Origin` wird aus einem Attachment erzeugt (`OriginAddFromAttachmentJob`).
2. Der binäre Stream wird über `IKnowledgeIngestor` normalisiert. Je nach `Docling:OutputFormat` wird der Markdown- oder der JSON-Ingestor registriert.
3. `DoclingHttpApiClient` ruft die Docling-Serve-API asynchron auf:
   - `POST v1/convert/file/async` (Multipart-Feld `files`, `output_format`, optional `image_export_mode`)
   - `GET v1/status/poll/{task_id}` (Polling-Intervall 1 s)
   - `GET v1/result/{task_id}`
4. Für Markdown werden **zwei** Konvertierungen durchgeführt (`placeholder` für den Text, `embedded` für die Bildextraktion); `DoclingImageExtractor` liest Base64-Daten aus dem Docling-JSON, dedupliziert per SHA-256 und leitet Dateinamen/Mime-Typen ab.
5. Bilder werden mit einem generierten Beschreibungstext als eigener Vektor in der Tabelle `image` gespeichert; die Binärdaten liegen im Storage-Service.

### 2. Chunking & Embedding

- `ChunkerRegistry` mappt Content-Type → Chunker-Typ; registriert ist `text/markdown` → `MarkdownChunker`.
- `MarkdownChunker` nutzt `TextChunker.SplitMarkDownLines` und `SplitMarkdownParagraphs` (`maxTokensPerChunk`, `overlapTokens`), Metadaten pro Chunk: `sourceType`, `chunkIndex`, `originId`.
- Jeder Chunk (und jedes Bild) wird einzeln über `IEmbeddingEngine.GetEmbeddingAsync(text, model)` vektorisiert und als `Chunk`- bzw. `Image`-Datensatz mit `vector(768)` persistiert.
- Die Embedding-Konfiguration (`model`, `active`, `top_k`, `max_tokens_per_chunk`, `overlap_tokens`) gehört zum `Embedding`-Datensatz, die Vektoren hängen an den Chunks.

### 3. Kontext-Erzeugung (RAG)

`ModelContextProvider` baut pro Tenant und `Context.Identifier`:

- **Begrüßung:** `Context.WelcomeMessage`
- **System-Prompt:** globale Origins (`origin.global_context = true`, sortiert nach `global_relevance`) → aktive Embeddings → Query-Vektor der Nutzereingabe → `FetchTopKSimilarChunksAsync` (pgvector **Cosine Distance**, `top_k` aus Embedding oder `ContextOptions.DefaultTopK`) → Chunk-Texte.
- Der Text wird mit **SmartFormat.NET** in `Context.SystemPromptTemplate` eingesetzt; verfügbare Platzhalter: `{GlobalContext}`, `{InitialPrompt}`.
- **Modell/Parameter:** `Context.Model` und `Context.NumCtx` steuern das Chat-Modell und die Kontextgröße pro Kontext.

### 4. Chat (interaktiv und API)

- **SignalR-Hub** `/ai/hubs/chat` (`[Authorize]`, Token auch per Query-Parameter `access_token` für Pfade unter `/ai/hubs`):
  - Client-Methoden: `Prompt(string message)`
  - Server-Events: `Response` (Text), `Error` (Fehlermeldung)
  - Session-Lebenszyklus: `ChatSessionStore` (Singleton, keyed nach `ConnectionId`) → `ChatSession` mit Lazy-Start der `IChatSession` bei der ersten Eingabe; der Context-Identifier ist hier fest `"global"`.
- **OpenAI-kompatibler Endpoint** `POST /ai/v1/chat/completions`: das Feld `model` wird als `Context.Identifier` interpretiert, die Nachrichten werden zu einem Prompt zusammengeführt; `stream: true` liefert SSE (`data: {…}` / `data: [DONE]`) im Format `chat.completion.chunk`.
- Inferenz: `ChatClientBuilder(...).UseFunctionInvocation()` – die Tool-Call-Schleife übernimmt `Microsoft.Extensions.AI`; `num_ctx` wird als AdditionalProperty durchgereicht. `AiChatSession` fängt Context-Length-Fehler (HTTP 400/413 mit `maximum context length`, `input_tokens`, `context length`) ab.

### 5. Tools & MCP

Zur Laufzeit werden pro Chat registriert:

| Namespace | Tool | Zweck |
|---|---|---|
| `math` | `add`, `subtract` | einfache Arithmetik (Demo-/Basisfunktionen) |
| `embeddings` | `origins.list` | listet abfragbare globale Origins inkl. Embedding-/Bildanzahl |
| `embeddings` | `origins.content` | liest Origin-Inhalt, optional `searchTerm`-Snippets und `maxChars` (1000–20000) |
| `embeddings` | `origins.query` | semantische Suche über aktive Embeddings (pgvector Cosine), `topK` 1–20 |

Zusätzlich werden externe **MCP-Server** aus `Mcp:Agents` eingebunden: je Agent ein benannter `HttpClient` (`mcp.{Namespace}`), Verbindung über `HttpClientTransport` (streamable HTTP, kein SSE). Mit `UserTokenDelegation` wird der Nutzer-Token als `Authorization: Bearer …` weitergegeben, sonst optional ein Client-Credentials-Handler (`ServiceCredentials`).

---

## Datenmodell

Alle Tabellen sind mandantenfähig (`tenant_id`, `uuid`, Audit-Spalten) und tragen einen Index auf `tenant_id` sowie einen Unique-Index auf `(tenant_id, uuid)`. Fremdschlüssel bestehen nicht; die Zuordnung erfolgt über Guid-Spalten.

| Tabelle | wesentliche Spalten |
|---|---|
| `context` | `identifier (100)`, `system_prompt_template`, `welcome_message`, `model (100)`, `num_ctx` — unique `(tenant_id, identifier)` |
| `origin` | `name (100)`, `description`, `content_type (100)`, `global_context`, `global_relevance`, `owner_entity/owner_id`, `refering_entity/refering_id` — unique `(tenant_id, owner_entity, owner_id, name)`; **keine Inhaltsspalte** |
| `embedding` | `origin_id`, `model`, `active`, `top_k`, `max_tokens_per_chunk`, `overlap_tokens` |
| `chunk` | `embedding_id`, `text (102400)`, `data vector(768)` |
| `image` | `origin_id`, `name (512)`, `description (4096)`, `content_type (100)`, `text (16384)`, `data vector(768)` — Index `(tenant_id, origin_id)` |

Beziehungen: `origin.uuid → embedding.origin_id → chunk.embedding_id` und `origin.uuid → image.origin_id`. Löschen eines Origin cascadiert Embeddings (inkl. Chunks) und Images.

**Payload-Auslagerung:** Der eigentliche Dokumenttext eines `Origin` und die Bildbinärdaten liegen nicht in PostgreSQL, sondern im **ballware Storage Service** (Attachment, Dateiname `payload<extension>`). `OriginPayloadRepositoryHook` lädt/schreibt den Text beim Lese-/Schreibzugriff über die Identifier `"primary"`; `ImagePayloadRepositoryHook` räumt Bild-Attachments beim Löschen auf.

---

## API-Übersicht

Zwei Swagger-Dokumente werden exponiert: **„llm“** (ballware LLM user API) und **„service“** (ballware LLM service API), OIDC-Security-Scheme mit PKCE-Support in der Swagger-UI.

| Route | Methode | Authentifizierung | Beschreibung |
|---|---|---|---|
| `/ai/hubs/chat` | SignalR | `[Authorize]` | interaktiver Chat-Hub (`Prompt` → `Response`/`Error`) |
| `/ai/v1/chat/completions` | POST | RequireAuthorization (Standard-Policy) | OpenAI-kompatible Chat-Completion, optional SSE-Streaming |
| `/ai/context/simulatesystemprompt` | POST | Policy `mlApi` | Dry-Run: baut System-Prompt und schätzt Tokens (ohne LLM-Aufruf) |
| `/ai/origin/addfromattachment` | POST | Policy `mlApi` | startet Quartz-Job `origin.addfromattachment` (202) |
| `/ai/origin/createembedding` | POST | Policy `mlApi` | startet Quartz-Job `origin.createembedding` (202) |
| `/ai/embedding/recreate` | POST | Policy `mlApi` | startet Quartz-Job `embedding.recreate` (201) |
| `/ai/context`, `/ai/origin`, `/ai/embedding`, `/ai/chunk`, `/ai/image` | CRUD | Policy `mlApi` | tenantfähige Editier-APIs aus `Ballware.Shared.Api.Endpoints` (List/Get/Create/Update/Delete, Import-Jobs) |

**Autorisierung:** JWT Bearer mit `Authority`/`Audience` (Issuer optional). Die Policies prüfen den `scope`-Claim gegen `Authorization:RequiredMetaScope` (`mlApi`, Nutzer-App) bzw. `RequiredServiceScope` (`serviceApi`, Service-zu-Service). Mandant, Nutzer und Rechte werden über `Ballware.Shared.Authorization` (`TenantClaim`, `UserIdClaim`, `RightClaim`) extrahiert; die Rechteprüfung erfolgt skriptbasiert (Jint) über Metadaten des **Meta Service** (`MetaServiceMetadataAdapter`).

---

## Hintergrundjobs (Quartz.NET)

Job-Status wird über den Meta Service gespiegelt (`JobCreateForTenantBehalfOfUserAsync` / `JobUpdate…`), Dateien über den Storage Service.

| JobKey | Job | Ablauf |
|---|---|---|
| `origin.addfromattachment` | `OriginAddFromAttachmentJob` | Attachment laden → Docling-Ingestion → `Origin` + Bilder (Vektoren + Upload) anlegen |
| `origin.createembedding` | `OriginCreateEmbeddingJob` | Origin chunken → `Embedding` anlegen → Chunk- und Bildvektoren schreiben |
| `embedding.recreate` | `EmbeddingRecreateJob` | alte Chunks löschen, Chunking-Parameter fortschreiben, neu chunken/vektorisieren |
| `origin.import`, `embedding.import` | `TenantableImportJob<…>` | Import von Origin/Embedding über die Editier-API |

Quartz ist auf `JobStoreTX` mit `PostgreSQLDelegate`, `clustered: true` und JSON-Serialisierung konfiguriert.

---

## Konfiguration

Konfigurationsquellen: `appsettings.json`, `appsettings.{Environment}.json`, `appsettings.local.json` (nicht versioniert), Umgebungsvariablen. Fehlende Pflichtabschnitte führen zu `ConfigurationException` mit Abort des Starts. Fast alle Optionen sind mit `ValidateOnStart` + DataAnnotations abgesichert.

| Abschnitt | Schlüssel | Pflicht |
|---|---|---|
| `Authorization` | `Authority`, `Audience`, `Issuer`, `TenantClaim` (default `tenant`), `UserIdClaim` (`sub`), `RightClaim` (`right`), `RequireHttpsMetadata`, `RequiredMetaScope` (`mlApi`), `RequiredServiceScope` (`serviceApi`) | Authority, Audience, Claims |
| `Llm` | `Provider` (`postgres`), `AutoMigrations` (default `false`) | `Provider` |
| ConnectionStrings | `LlmStorageConnection` | ja |
| `Inference` | `Endpoint`, `ApiKey`, `Model` | Endpoint, ApiKey |
| `Embedding` | `Endpoint`, `ApiKey`, `Model` | Endpoint, ApiKey |
| `Docling` | `Url`, `OutputFormat` (`markdown` \| `json`), `RequestTimeoutSeconds` (120) | Url, OutputFormat |
| `Context` | `DefaultTopK` (5) | – |
| `Chunking` | `DefaultMaxTokensPerChunk` (500), `DefaultOverlapTokens` (50) | – |
| `Mcp` | `Agents[]`: `Namespace`, `ServiceUrl`, `UserTokenDelegation`, `CredentialIdentifier` | – |
| `ServiceCredentials` | `Credentials[]`: `Identifier`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scopes` | – |
| `MetaClient` / `StorageClient` | `ServiceUrl`, `CredentialIdentifier`, `ForwardUserIdentity` | ja (MetaClient/StorageClient) |
| `Swagger` | `EnableClient`, `ClientId`, `ClientSecret`, `RequiredScopes` | – |
| `Cors` | `AllowedOrigins`, `AllowedMethods`, `AllowedHeaders` (Komma-Listen) | – |
| `Quartz` | JobStore/Scheduler-Einstellungen (PostgreSQL, clustered) | – |
| `Serilog` / `Logging` | Log-Level und Console-Sink | – |

Die `Endpoint`-Angaben von `Inference` und `Embedding` werden automatisch um `/v1` ergänzt, falls nicht bereits vorhanden — dadurch funktionieren OpenAI-Endpunkte, Azure-OpenAI-Gateways und kompatible Server (z. B. Ollama, vLLM, LiteLLM).

---

## Voraussetzung / Abhängige Dienste

- PostgreSQL mit der Erweiterung **`vector`** (pgvector). Die Erweiterung wird im `LlmDbContext` deklariert (`HasPostgresExtension("vector")`); Migrationen laufen nur bei `Llm:AutoMigrations = true` über den `InitializationWorker`.
- **Docling Serve** (Dokumentkonvertierung, `llm/serve` API)
- **ballware Meta Service** (Mandanten-/Entitätsmetadaten, Rechte-Skripte, Job- und Export-Verwaltung)
- **ballware Storage Service** (Attachments, Bildbinärdaten)
- OIDC-Autorität (Token-Issuer) mit Client-Credentials für Service-zu-Service-Aufrufe
- NuGet-Feed `https://nuget.pkg.github.com/ballware/…` für die `ballware.*`-Pakete

---

## Build & Run

```bash
# Build der Solution (.NET SDK 10, rollForward: latestMajor)
dotnet build ballware-llm-service.sln -c Release

# Service starten
dotnet run --project src/Ballware.Llm.Service

# Tests (NUnit, benötigt Docker für Testcontainers/WireMock)
dotnet test test/Ballware.Llm.Ingestion.Docling.Tests
```

Container:

```bash
docker build -f src/Ballware.Llm.Service/Dockerfile src/. \
  --build-arg GITHUB_USERNAME=<user> --build-arg GITHUB_PAT=<token> \
  -t ballware-llm-service
docker run -p 5000:5000 ballware-llm-service
```

Das Multi-Stage-Dockerfile nutzt `dotnet/aspnet:10.0` / `dotnet/sdk:10.0`, legt `EXPOSE 5000` und `5001`, führt als Nicht-Root-User `app` aus und startet `Ballware.Llm.Service.dll`.

## CI/CD

`.github/workflows/publish-packages.yml` läuft bei Push auf `main` und `develop`:

- **build_and_pack**: GitVersion (SemVer), Build der Solution, Versionstag `v<semver>` auf `main`. Swagger-/NSwag-Client-Generierung und `dotnet pack`/`nuget push` sind vorhanden, aber aktuell deaktiviert.
- **docker_publish**: Multi-Arch-Build (`linux/amd64`, `linux/arm64`) und Push nach `ghcr.io/ballware/ballware-llm-service` mit Tags `<semver>` plus `stable` (main) bzw. `latest` (develop).

---

## Hinweise / aktuelle Einschränkungen

- **Streaming ist nominell:** `AiChatSession` sammelt die Streaming-Antwort vollständig und gibt sie in einem Callback weiter. Der SignalR-Hub sendet deshalb pro Prompt ein einziges `Response`-Event, die SSE-Variante einen Content-Chunk plus `stop`-Chunk und `[DONE]`.
- **Kein mehrstufiges Gedächtnis:** jede Anfrage wird als Einzelnachricht gesendet; Gesprächskontext entsteht nur über den wieder verwendeten `IChatSession`-Objekt (System-Prompt und Tools werden beim ersten Prompt fixiert).
- Der SignalR-Chat nutzt fest den Context `"global"`; der OpenAI-Endpoint verwendet `body.model` als Context-Identifier.
- Die Token-Schätzung ist eine Näherung (`Länge/4` bzw. `(Länge+3)/4`) und dient nur Logging/Preview-Zwecken.
- `Embedding.top_k` wird aus der Request-Modell-Validierung (`OriginCreateEmbeddingRequest.TopK`) nicht an den Job weitergegeben; effektiv greift `Context:DefaultTopK`.
- Für die Vektorsuche existiert kein ANN-Index (kein HNSW/IVFFlat) — Ähnlichkeitssuche als Seq-Scan mit `ORDER BY` Cosine-Distanz.
- Unbenutzte Verträge: `IModelToolProvider`, `IChatSessionBuilder`/`IChatSessionBuilderStage` und `Ballware.Llm.Metadata.Attachment` sind deklariert, aber nicht implementiert/verwendet.

---

## Lizenz

MIT — siehe [LICENSE](LICENSE). © ballware Software & Consulting
