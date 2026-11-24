# Architecture

## Two composition roots, one shared wiring layer

| Host | Entry point | What it exposes |
| --- | --- | --- |
| `src/Ballware.Llm.Service` | `Program.cs` + `Startup.cs` | the documented HTTP API (71 paths), the SignalR hub, Swagger, Quartz jobs |
| `src/Ballware.Llm.App` | `Program.cs` + `Startup.cs` | a Blazor Server chat UI (`/chat/{contextIdentifier}`), cookie/OIDC browser sign-in; **no** API endpoints, no Swagger, no jobs — see [chat-application.md](chat-application.md) |

Both hosts call the same extension methods from `src/Ballware.Llm.Hosting/LlmHostingExtensions.cs`,
which is why the two hosts behave identically for context, inference, tools, data access, storage and
authorization wiring:

| Method | Registers | Lines |
| --- | --- | --- |
| `AddBallwareLlmCoreConfiguration` | `LlmStorageOptions` (`Llm`), `ContextOptions` (`Context`), `ChunkingOptions` (`Chunking`), `OpenAiInferenceOptions` (`Inference`), `OpenAiEmbeddingOptions` (`Embedding`), `McpOptions` (`Mcp`), `ServiceCredentialOptions` (`ServiceCredentials`), `ServiceClientOptions` (`MetaClient`), memory caches, `HttpContextAccessor`, CORS policy; hard guards for `Llm`+`LlmStorageConnection`, `Inference`, `Embedding`, `MetaClient`, `StorageClient` | `:38-125` |
| `AddBallwareLlmClients` | Duende client-credentials token management from `ServiceCredentials.Credentials[]`, typed `HttpClient<StorageServiceClient>`, typed `HttpClient<MetaServiceClient>`, one named `HttpClient` per MCP agent (`mcp.{Namespace}`) | `:127-220` |
| `AddBallwareLlmDataStore` | provider switch on `Llm.Provider`; only `"postgres"` is implemented, any other value silently registers nothing (`:232-237`) | `:222-238` |
| `AddBallwareLlmSharedAuthorizationUtils` | Jint rights checkers + `IPrincipalUtils` with the configured claim names | `:240-254` |
| `AddBallwareLlmMetadataAndStorageAdapters` | Mapster `TypeAdapterConfig` + `IMapper`, `MetaServiceMetadataAdapter` (`IAuthorizationMetadataProvider`, `IExportMetadataProvider`), `StorageServiceFileStorageAdapter` (`IFileStorageProvider`), `OriginPayloadRepositoryHook`, `ImagePayloadRepositoryHook` | `:256-274` |
| `AddBallwareLlmTools` | `MathToolProvider` (singleton), `EmbeddingsToolProvider` (scoped) | `:276-282` |
| `AddBallwareLlmContextAndInference` | `IModelContextProvider`, `IEmbeddingEngine` → `OpenAiEmbeddingEngine` (not configurable), `IInferenceEngine` → `AiInferenceEngine` (not configurable), `ChunkerRegistry` + `MarkdownChunker` for `"text/markdown"`, tool/MCP registration | `:284-327` |

The Service adds the API/job-specific pieces itself: `AddMvcCore().AddApiExplorer()`, `AddSignalR()`,
`AddControllers()`, `Configure<QuartzOptions>("Quartz")`, `AddBallwareLlmBackgroundJobs()`,
`AddBallwareSharedApiDependencies()`, `AddBallwareLlmApiDependencies()`, `AddEndpointsApiExplorer()`,
`AddHttpClient<DoclingApiClient>`, and the job adapters `IJobMetadataProvider` / `IJobFileStorageAdapter`
(`Startup.cs:131-166`, `:158-159`).

## Project layout

Ports & adapters: contracts in the neutral projects, implementations in the `.OpenAi` /
`.SemanticKernel` / `.Docling` / `.Postgres` projects.

| Project | Role |
| --- | --- |
| `Ballware.Llm.Service` | composition root: HTTP host, auth, Swagger, job adapters, Docker entry point |
| `Ballware.Llm.App` | composition root: Blazor Server chat host |
| `Ballware.Llm.Hosting` | shared DI wiring, Meta/Storage adapters, payload hooks, hosting config models |
| `Ballware.Llm.Api` | minimal-API endpoints, `ChatHub`, session store, request/response models |
| `Ballware.Llm.Inference` / `.Inference.OpenAi` | inference contracts / OpenAI-compatible engine, tool registry, MCP client use |
| `Ballware.Llm.Embedding` / `.Embedding.OpenAi` / `.Embedding.SemanticKernel` | embedding + chunking contracts / OpenAI embeddings / `MarkdownChunker` |
| `Ballware.Llm.Context` | system-prompt composition and token estimation |
| `Ballware.Llm.Tools` / `.Tools.Common` / `.Tools.Embedding` | tool contracts / math tools / knowledge tools |
| `Ballware.Llm.Ingestion` / `.Ingestion.Docling` | ingestion contracts / Docling Serve client wrapper |
| `Ballware.Llm.Docling.Client` | NSwag-generated Docling Serve client + hand-written extensions |
| `Ballware.Llm.Data` / `.Data.Ef` / `.Data.Ef.Postgres` | models and repository contracts / EF repositories and mapping / DbContext, pgvector, migrations |
| `Ballware.Llm.Jobs` | Quartz job definitions |
| `Ballware.Llm.Metadata` | `Tenant`, `EntityMetadata`, `Attachment` authorization DTOs |
| `Ballware.Llm.Ingestion.Docling.Tests` | NUnit + Testcontainers (WireMock) tests for the Docling ingestion path |

**Not part of the build:** `src/Ballware.Assistant`, `src/Ballware.Assistant.Workflow`,
`src/Ballware.Assistant.Workflow.MsAgentNetwork`, `src/Ballware.Llm.Engine.Mcp` contain only `bin/` and
`obj/` artifacts (no `.csproj`, no `.cs` outside `obj/`) and are absent from
`ballware-llm-service.sln` (21 `src/` projects + 1 test project are in the solution). They are dead
folders and are not documented here. MCP functionality lives in
`Ballware.Llm.Inference.OpenAi` + `Ballware.Llm.Tools` + `Ballware.Llm.Hosting`.

## Startup and configuration order

```text
Program.cs:9-25   WebApplication builder; Sources.Clear(); appsettings.json
                  → appsettings.{Environment}.json → appsettings.local.json → env vars
                  → Kestrel bound from the "Kestrel" section
Program.cs:29-37  Startup.InitializeServices(); a ConfigurationException prints and the process
                  exits with -1 (missing Authorization / Docling / Llm+connection / Inference /
                  Embedding / MetaClient / StorageClient)
Startup.cs:43-67  options bound with AddOptionsWithValidateOnStart + ValidateDataAnnotations
                  (AuthorizationOptions, SwaggerOptions, DoclingOptions)
Startup.cs:69-166 hosting/DI wiring (see table above)
Startup.cs:168-200 Swagger documents "llm" and "service", oidc security scheme
Startup.cs:203-277 pipeline
```

`appsettings.json` (`src/Ballware.Llm.Service/appsettings.json`) contains **only** `Kestrel.Limits`,
`Logging`, `Serilog`, `Quartz` and `AllowedHosts` — every functional section (`Authorization`, `Llm`,
`ConnectionStrings`, `Inference`, `Embedding`, `Docling`, `MetaClient`, `StorageClient`) must come from
the environment-specific file, `appsettings.local.json` (git-ignored), or environment variables.

## Request pipeline

```text
DeveloperExceptionPage (Development only)                          Startup.cs:205-209
UseExceptionHandler → ProblemDetails                               Startup.cs:211-236
UseCors  (policy from the "Cors" section, only if the section exists)  Startup.cs:238, LlmHostingExtensions.cs:110-122
UseRouting → UseAuthentication → UseAuthorization                  Startup.cs:239-242
endpoints: hub + LLM operations + 5 generic editing mounts         Startup.cs:244-254
UseSwagger (+ UseSwaggerUI when Swagger.EnableClient)              Startup.cs:259-275
```

CORS runs **before** routing; values from `Cors.AllowedOrigins`/`AllowedMethods`/`AllowedHeaders` are
split on `,` and `AllowCredentials()` is always added (`LlmHostingExtensions.cs:116-119`). The Swagger
environment ships `"*"` values (`appsettings.Swagger.json:11-15`).

## Hosted services

| Hosted service | Registered by | Behavior |
| --- | --- | --- |
| `InitializationWorker` | `Ballware.Llm.Data.Ef.Postgres/ServiceCollectionExtensions.cs:48` | applies EF migrations on start **only** when `Llm.AutoMigrations` is `true` (default `false`) — `Internal/InitializationWorker.cs:17-29` |
| `OllamaToolRegistryInitializer` | `Ballware.Llm.Inference.OpenAi/ServiceCollectionExtensions.cs:23-26` | runs the tool/MCP registration callback at host start (legacy name; the engine is OpenAI-compatible) |
| Quartz server | `Ballware.Llm.Jobs/ServiceCollectionExtensions.cs:25-28` | `WaitForJobsToComplete = true` — running jobs are given time to finish on shutdown |

## Error contract

Unhandled exceptions are logged (`Log.Error`) and answered with `application/problem+json`
(`Startup.cs:211-236`):

```json
{ "type": "https://httpstatuses.com/500", "title": "An unexpected error occurred.", "status": 500, "detail": "<exception text, Development only>", "instance": "/ai/origin/all" }
```

`detail` is populated only in Development (`Startup.cs:227`). Expected per-operation codes (401/404/400)
are tabulated in [tenancy-and-authorization.md](tenancy-and-authorization.md).

## Ports

| Context | Value | Source |
| --- | --- | --- |
| Service, Development | `http://localhost:5006` | `src/Ballware.Llm.Service/appsettings.Development.json:15-21` |
| Service, container | `EXPOSE 5000`, `EXPOSE 5001` (no `Kestrel.Endpoints` in the tracked config → ASP.NET defaults) | `src/Ballware.Llm.Service/Dockerfile:3-4`, `appsettings.json:2-7` |
| App, all environments | `http://+:3004` | `src/Ballware.Llm.App/appsettings.json:5` |
| App, container | `EXPOSE 5000`, `EXPOSE 5001` — **inconsistent** with the `3004` listener | `src/Ballware.Llm.App/Dockerfile:11-12` |

Request size limits: `MaxRequestLineSize: 24576`, `MaxRequestBodySize: 40000000`
(`src/Ballware.Llm.Service/appsettings.json:3-6`).

## Related pages

- [tenancy-and-authorization.md](tenancy-and-authorization.md) — the authorization stage in detail
- [data-model.md](data-model.md), [payload-offloading.md](payload-offloading.md) — the data stage
- [rag-context-and-tools.md](rag-context-and-tools.md), [knowledge-pipeline.md](knowledge-pipeline.md),
  [background-jobs.md](background-jobs.md) — the inference and ingestion paths
- [chat-application.md](chat-application.md) — the second host
- [upstream-dependencies.md](upstream-dependencies.md) — what the service calls
