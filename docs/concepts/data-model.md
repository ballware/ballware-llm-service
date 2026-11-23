# Data model

PostgreSQL via EF Core 10 + Npgsql + `Pgvector.EntityFrameworkCore`. All tables and columns below are
taken from the migrations in `src/Ballware.Llm.Data.Ef.Postgres/Migrations/` and the model
configuration in `src/Ballware.Llm.Data.Ef/Model/LlmModelBaseCustomizer.cs`, `src/Ballware.Llm.Data/Persistables/*.cs`,
`src/Ballware.Llm.Data.Ef.Postgres/LlmDbContext.cs`.

## Shared shape

Every table follows the same pattern:

| Column | Postgres type | Notes |
| --- | --- | --- |
| `id` | `bigint` | identity-by-default column, primary key (`PK_<table>`) — internal surrogate key, **never exposed by the API** |
| `uuid` | `uuid` | the public id; the API's `Id` field maps to it (`src/Ballware.Llm.Data.Ef/Mapping/StorageMappingProfile.cs:12-16`) |
| `tenant_id` | `uuid` | tenant scope, applied in every repository query |
| `creator_id`, `last_changer_id` | `uuid NULL` | audit |
| `create_stamp`, `last_change_stamp` | `timestamp with time zone NULL` | normalized to UTC by value converters (`LlmModelBaseCustomizer.cs:23-41`) |

Indexes: `(<table>.tenant_id)` and a unique `(tenant_id, uuid)` on every table
(`LlmModelBaseCustomizer.cs:43-64`). **No foreign keys exist anywhere** — relationships are plain uuid
columns (verified: no `ForeignKey`/`HasForeignKey`/`OnDelete` in `src/Ballware.Llm.Data*`, and no
`REFERENCES` in any migration).

## `context`

`src/Ballware.Llm.Data/Persistables/Context.cs`, created by
`Migrations/20260204164036_ContextMetadata.cs:15-34`, extended by
`20260208105528_ContextAdditionalOptions.cs:13-24`.

| Column | Type | Constraints |
| --- | --- | --- |
| `identifier` | `character varying(100)` | NOT NULL |
| `system_prompt_template` | `text` | NULL |
| `welcome_message` | `text` | NULL |
| `model` | `character varying(100)` | NULL |
| `num_ctx` | `integer` | NULL |

Indexes: `IX_context_tenant_id`, `IX_context_tenant_id_identifier` (unique), `IX_context_tenant_id_uuid`
(unique). `identifier` is what the chat API addresses: `body.model` on
`POST /ai/v1/chat/completions`, `ContextIdentifier` on `simulatesystemprompt`, and the literal `global`
for the SignalR hub.

## `origin`

`src/Ballware.Llm.Data/Persistables/Origin.cs`, created by `20260131121545_Initial.cs:51-67`, extended by
`20260131130711_OriginPayload.cs`, `20260201081150_OriginContentType.cs`, `20260203071947_OriginGlobalContext.cs`,
`20260209075358_OriginAddGlobalRelevance.cs`.

| Column | Type | Constraints |
| --- | --- | --- |
| `name` | `character varying(100)` | NOT NULL |
| `description` | `text` | NOT NULL (migration default `""`; the ingestion job writes the literal `None`) |
| `owner_entity` | `character varying(100)` | NOT NULL — `attachment` for ingested origins |
| `owner_id` | `uuid` | NOT NULL |
| `refering_entity` | `character varying(100)` | NOT NULL — the entity the ingestion was requested for |
| `refering_id` | `uuid` | NOT NULL |
| `content_type` | `character varying(100)` | NOT NULL, default `""` — e.g. `text/markdown` |
| `global_context` | `boolean` | NOT NULL, default `false` — injected into every system prompt |
| `global_relevance` | `integer` | NULL — ordering key, **ascending**, `null` treated as `0` (`ModelContextProvider.cs:27`) |

Index: `IX_origin_tenant_id_owner_entity_owner_id_name` (unique) in addition to the shared two
(`20260131130711_OriginPayload.cs:56-60`).

**There is no content column.** Despite its name, `20260131130711_OriginPayload` adds only the
owner/refering/name/description columns. The document text lives in the Storage Service —
[payload-offloading.md](payload-offloading.md).

## `embedding`

`src/Ballware.Llm.Data/Persistables/Embedding.cs`, created by `20260131121545_Initial.cs:33-49`, extended
by `20260202154357_EmbeddingAndChunkPayload.cs:15-34`, `20260209075024_EmbeddingAddOptionalTopK.cs`,
`20260212134017_EmbeddingAddChunkSettings.cs`.

| Column | Type | Constraints |
| --- | --- | --- |
| `origin_id` | `uuid` | NOT NULL, no FK |
| `model` | `text` | NOT NULL (CLR type is `string?` but carries `[Required]` — `Persistables/Embedding.cs:23-25`) |
| `active` | `boolean` | NOT NULL, default `false` — only active embeddings are retrieved (`EmbeddingBaseRepository.cs:50-56`) |
| `top_k` | `integer` | NULL — read as `embedding.TopK ?? ContextOptions.DefaultTopK` (`ModelContextProvider.cs:41`) |
| `max_tokens_per_chunk` | `integer` | NULL |
| `overlap_tokens` | `integer` | NULL |

## `chunk`

`src/Ballware.Llm.Data/Persistables/Chunk.cs`, created by `20260131121545_Initial.cs:15-31`, extended by
`20260202154357_EmbeddingAndChunkPayload.cs:36-47`, `20260202154854_ChunkText.cs`,
`20260203071256_IncreaseChunkTextSize.cs`.

| Column | Type | Constraints |
| --- | --- | --- |
| `embedding_id` | `uuid` | NOT NULL, no FK |
| `text` | `character varying(102400)` | NOT NULL (raised from 1024 in `20260203071256`) |
| `data` | **`vector(768)`** | NOT NULL (`20260202154357_EmbeddingAndChunkPayload.cs:36-40`, `table.Column<Vector>`) |

Only the shared indexes exist — **no index on `embedding_id`**, and no vector index
(`LlmModelBaseCustomizer.cs:57-59`). Retrieval:

```csharp
dbContext.Set<Persistables.Chunk>()
    .Where(c => c.TenantId == tenantId && c.EmbeddingId == embeddingId)
    .OrderBy(c => c.Data.CosineDistance(queryVector))
    .Take(topK)
    .Select(c => c.Text)
```

(`src/Ballware.Llm.Data.Ef.Postgres/Repository/ChunkRepository.cs:15-27`) — a filtered sequential scan
ordered by pgvector cosine distance; the distance value is discarded (only text is returned).

## `image`

`src/Ballware.Llm.Data/Persistables/Image.cs`, created by `20260429051750_AddImageExtractionFromOrigins.cs:16-54`.

| Column | Type | Constraints |
| --- | --- | --- |
| `origin_id` | `uuid` | NOT NULL, no FK |
| `name` | `character varying(512)` | NOT NULL |
| `description` | `character varying(4096)` | NOT NULL |
| `content_type` | `character varying(100)` | NOT NULL |
| `text` | `character varying(16384)` | NOT NULL — the labelled description text that was embedded |
| `data` | **`vector(768)`** | NOT NULL |

Indexes: `IX_image_tenant_id`, `IX_image_tenant_id_origin_id` (**non-unique**), `IX_image_tenant_id_uuid`
(unique). Same cosine-distance pattern for `FetchTopKSimilarImagesAsync`
(`src/Ballware.Llm.Data.Ef.Postgres/Repository/ImageRepository.cs:13-25`), which currently has **no
caller** in the ingestion or context path (only the tools/retrieval layer uses chunks).

## pgvector

`LlmDbContext.cs:12` declares `modelBuilder.HasPostgresExtension("vector")`, and
`Data.Ef.Postgres/ServiceCollectionExtensions.cs:20-29` enables `UseVector()`. **No migration creates the
extension** (no `PostgresExtension` operation in `Migrations/`), so the database must already have
`vector` available — it is a deployment prerequisite, not something the service installs.

Dimension 768 is fixed by the column type only. Nothing in code or config validates it
(`IEmbeddingEngine.GetEmbeddingAsync` returns `float[]` of whatever length the embedding endpoint
returns — `src/Ballware.Llm.Embedding.OpenAi/Internal/OpenAiEmbeddingEngine.cs:45-55`), so a model with
a different dimensionality fails at insert/update time, not at startup.

## Indexes: no ANN

Searching the migrations and model customizer for `ivfflat`, `hnsw`, `gin`, `CREATE INDEX` yields only
plain b-tree indexes. Every similarity query is a sequential scan of the tenant+embedding subset with a
sort. Cost therefore grows linearly with chunk count per embedding.

## Relationships and cascades

Logical relationships (no database enforcement):

```text
origin.uuid ─→ embedding.origin_id ─→ chunk.embedding_id
origin.uuid ─→ image.origin_id
```

Cascade rules are implemented in repositories:

| Deleting | Also deletes | Source |
| --- | --- | --- |
| an origin | its embeddings, each embedding's chunks, its images | `src/Ballware.Llm.Data.Ef/Repository/OriginBaseRepository.cs:12-42` |
| an embedding | its chunks | `src/Ballware.Llm.Data.Ef/Repository/EmbeddingBaseRepository.cs:31-48` |
| a chunk / an image | nothing | — |

`EmbeddingRecreateJob` clears chunks with `ExecuteDeleteAsync` by `embedding_id` before re-embedding
(`src/Ballware.Llm.Jobs/Internal/EmbeddingRecreateJob.cs:49`,
`ChunkRepository.cs:29-36`).

> Because there are no FKs, deleting rows directly in PostgreSQL leaves `embedding`, `image` and `chunk`
> rows orphaned, and the Storage Service attachments are not touched either. Only the API/repository
> path cascades correctly.

## Public vs Persistables models

API contracts are the `Ballware.Llm.Data.Public` models; EF maps the `Persistables` ones. Mapster
bridges them (`src/Ballware.Llm.Data.Ef/Mapping/StorageMappingProfile.cs`):

| Aspect | Persistables | Public |
| --- | --- | --- |
| identity | `long? Id` (db `id`) **and** `Guid Uuid` | single `Guid Id` ← `Uuid` (`:15-16`) |
| tenancy/audit | `TenantId`, `CreatorId`, `CreateStamp`, `LastChangerId`, `LastChangeStamp` | absent |
| annotations | `[Table]`, `[Column]`, `[Required]`, `[MaxLength]`, `TypeName` | absent |
| vectors | `Pgvector.Vector Data` | `float[] Data` (`:35`, `:39`, `:48`) |
| payloads | no `Content` column on `Origin` | `Origin.Content` (external payload only) |

Repository interfaces the API uses: `IContextRepository`, `IOriginRepository`, `IEmbeddingRepository`,
`IChunkRepository`, `IImageRepository` (`src/Ballware.Llm.Data/Repository/*.cs`), all extending the
shared `ITenantableRepository<T>`.

## Migration history

`src/Ballware.Llm.Data.Ef.Postgres/Migrations/` (13 migrations, EF Core `ProductVersion` 10.0.2 → 10.0.3):

| # | Migration | Effect |
| --- | --- | --- |
| 1 | `20260131121545_Initial` | creates `chunk`, `embedding`, `origin` with the shared columns only |
| 2 | `20260131130711_OriginPayload` | origin `name`/`description`/`owner_*`/`refering_*`, unique index `(tenant_id, owner_entity, owner_id, name)` |
| 3 | `20260201081150_OriginContentType` | text → `varchar(100)` for name/entity columns, adds `content_type` |
| 4 | `20260202154357_EmbeddingAndChunkPayload` | `embedding.active/model/origin_id`, `chunk.data vector(768)`, `chunk.embedding_id` |
| 5 | `20260202154854_ChunkText` | `chunk.text varchar(1024)` |
| 6 | `20260203071256_IncreaseChunkTextSize` | `chunk.text varchar(102400)` |
| 7 | `20260203071947_OriginGlobalContext` | `origin.global_context` |
| 8 | `20260204164036_ContextMetadata` | creates `context` + unique `(tenant_id, identifier)` |
| 9 | `20260208105528_ContextAdditionalOptions` | `context.model`, `context.num_ctx` |
| 10 | `20260209075024_EmbeddingAddOptionalTopK` | `embedding.top_k` |
| 11 | `20260209075358_OriginAddGlobalRelevance` | `origin.global_relevance` |
| 12 | `20260212134017_EmbeddingAddChunkSettings` | `embedding.max_tokens_per_chunk`, `embedding.overlap_tokens` |
| 13 | `20260429051750_AddImageExtractionFromOrigins` | creates `image` incl. `data vector(768)` |

Migrations are applied only when `Llm.AutoMigrations = true` (`Llm.Provider` must be `postgres`;
[architecture.md](architecture.md)). `src/Ballware.Llm.App/appsettings.json:62` ships `false`.

## Related pages

- [payload-offloading.md](payload-offloading.md) — where `Origin.Content` and image binaries live
- [knowledge-pipeline.md](knowledge-pipeline.md) — how rows are produced
- [rag-context-and-tools.md](rag-context-and-tools.md) — how rows are consumed
