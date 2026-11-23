# Background jobs

All asynchronous work runs on Quartz.NET. The Service configures the scheduler from the `Quartz`
configuration section (`src/Ballware.Llm.Service/Startup.cs:145`) and registers the jobs with
`AddBallwareLlmBackgroundJobs()` (`Startup.cs:146`,
`src/Ballware.Llm.Jobs/ServiceCollectionExtensions.cs:12-28`). The App host registers **no** jobs.

## Registration model

Every job is registered with `configurator.StoreDurably()` and **no trigger and no schedule**
(`Ballware.Llm.Jobs/ServiceCollectionExtensions.cs:18-22`) — nothing runs on a cron. Jobs fire only when an
endpoint (or the shared import handler) calls `TriggerJob(jobKey, jobDataMap)`.

| Job key (name, group) | Job type | Triggered by |
| --- | --- | --- |
| `("addfromattachment", "origin")` | `OriginAddFromAttachmentJob` | `POST /ai/origin/addfromattachment` (`OriginEndpoint.cs:67`) |
| `("createembedding", "origin")` | `OriginCreateEmbeddingJob` | `POST /ai/origin/createembedding` (`OriginEndpoint.cs:102`) |
| `("recreate", "embedding")` | `EmbeddingRecreateJob` | `POST /ai/embedding/recreate` (`EmbeddingEndpoint.cs:55`) |
| `("import", "origin")` | `TenantableImportJob<Public.Origin, …>` (shared package) | `POST /ai/origin/import` |
| `("import", "embedding")` | `TenantableImportJob<Public.Embedding, …>` (shared package) | `POST /ai/embedding/import` |

`import` job keys exist for `origin` and `embedding` only; the generic `import` route is also mounted for
`context`, `chunk` and `image`, but no matching job key exists, so those triggers fail in the scheduler.

The Quartz server is started with `options.WaitForJobsToComplete = true` (`:25-28`).

## Scheduler configuration

Tracked config (`src/Ballware.Llm.Service/appsettings.json:33-39`):

```json
"Quartz": {
  "quartz.scheduler.instanceId": "AUTO",
  "quartz.serializer.type": "json",
  "quartz.jobStore.type": "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
  "quartz.jobStore.driverDelegateType": "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz",
  "quartz.jobStore.clustered": true
}
```

This declares a **clustered, persistent** job store, but the tracked file supplies no data source. To run
it, a deployment must also provide (`src/Ballware.Llm.Service/appsettings.local.json` does, key names
only):

```text
Quartz:quartz.scheduler.instanceName
Quartz:quartz.jobStore.dataSource                       = <dataSourceName>
Quartz:quartz.dataSource.<dataSourceName>.provider     = Npgsql
Quartz:quartz.dataSource.<dataSourceName>.connectionStringName = <connection string key>
ConnectionStrings:<connection string key>
```

Two consequences:

1. **The Quartz tables are not created by this repo.** The migrations in
   `src/Ballware.Llm.Data.Ef.Postgres/Migrations/` create only `context`, `origin`, `embedding`, `chunk`,
   `image` — no `QRT_*` tables. A clustered `JobStoreTX` needs them provisioned separately.
2. The Swagger environment switches to an in-memory store
   (`appsettings.Swagger.json:16-18`: `quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz`), which is
   what allows `swagger tofile` to run without a database.

## Job payloads

### `addfromattachment` / `origin` — `OriginAddFromAttachmentJob`

| `JobDataMap` key | Type | Source |
| --- | --- | --- |
| `tenantId`, `userId` | guid | `OriginEndpoint.cs:60-61` |
| `ownerEntity`, `name` | string (nullable → `ArgumentNullException`, `:28-36`) | `:62, 65` |
| `ownerId`, `attachmentId` | guid | `:63-64` |

Pipeline: read attachment → ingest (Docling) → write `origin` (+ payload upload) → write `image` rows and
upload binaries. Full detail in [knowledge-pipeline.md](knowledge-pipeline.md). Guard: a missing
attachment stream raises `Exception("Attachment could not be found")` (`:40-43`).

### `createembedding` / `origin` — `OriginCreateEmbeddingJob`

| `JobDataMap` key | Type | Notes |
| --- | --- | --- |
| `tenantId`, `userId`, `id` | guid | `id` is the **origin** uuid |
| `model` | string | `null` → `ArgumentNullException` (`:41-44`) |
| `activate` | bool | becomes `Embedding.Active` |
| `tokensPerChunk`, `overlapTokens` | int, optional | `TryGetIntValue` (`:31-39`); `null` reaches the chunker, which applies the `Chunking` defaults |

`topK` is never a job key (the endpoint drops it) — see
[../api/llm-api-resources.md](../api/llm-api-resources.md).

### `recreate` / `embedding` — `EmbeddingRecreateJob`

| `JobDataMap` key | Type | Notes |
| --- | --- | --- |
| `tenantId`, `userId`, `id` | guid | `id` is the **embedding** uuid |

Uses `ChunkingOptions` injected into the job (`EmbeddingRecreateJob.cs:17`) and writes the effective
chunk sizes back onto the embedding row (`:55-59`).

### `import` / `origin` and `import` / `embedding` — `TenantableImportJob` (shared package)

| `JobDataMap` key | Type | Source |
| --- | --- | --- |
| `tenantId`, `userId`, `jobId` | guid | `TenantableEndpointHandlerFactory.cs:200-214` |
| `identifier` | string, optional | the `import` identifier |
| `claims` | string (JSON) | the caller's claim map, serialized so the job can re-run the rights check |
| `file` | guid | temporary file id in the Storage Service |

The job checks `meta.<entity>.<identifier>` per item — note the hard-coded `"meta"` application
(`ballware-shared-dotnet/src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs:67`) — and stages files
through the `IFileStorageProvider` (see [payload-offloading.md](payload-offloading.md)).

## Job state visibility

**Only the import jobs report status.** `IJobMetadataProvider` is registered in the Service host
(`Startup.cs:158` → `MetaServiceMetadataAdapter`), and the shared `TenantableImportJob` uses it:

| Point | Call | Meta Service payload |
| --- | --- | --- |
| job creation (at the HTTP handler, not in a job) | `CreateJobAsync(tenantId, userId, "document", "import", <serialized jobData>)` (`TenantableEndpointHandlerFactory.cs:200-214`) | `Scheduler = "document"`, `Identifier = "import"`, `Options = <job data JSON>`; the returned id is put back into the `JobDataMap` as `jobId` |
| start | `UpdateJobAsync(…, JobStates.InProgress, "")` (`TenantableImportJob.cs:61`) | |
| success | `UpdateJobAsync(…, JobStates.Finished, "")` (`:73`) | |
| failure | `UpdateJobAsync(…, JobStates.Error, <serialized exception>)`, then rethrow as `JobExecutionException(refireImmediately: false)` (`:79-83`) | |

`JobStates` values in the shared package: `Unknown = 0`, `Queued = 1`, `InProgress = 5`, `Finished = 10`,
`Error = 99`. The enum conversion to the Meta Service client's enum happens through Mapster's default
mapping — no explicit enum map exists in `MetaServiceLlmMetadataProfile.cs`.

The three LLM jobs (`addfromattachment`, `createembedding`, `recreate`) **do not** inject
`IJobMetadataProvider` (`OriginAddFromAttachmentJob.cs:10-15`, `OriginCreateEmbeddingJob.cs:9-16`,
`EmbeddingRecreateJob.cs:10-18`) and write no job record, have **no** `try/catch` and never raise
`JobExecutionException`. Practical consequences:

- There is no progress or status endpoint for these jobs; poll the resources instead
  ([../api/examples.md](../api/examples.md)).
- A failure is only visible in the service log and in a Quartz error entry (persistent store), and the
  exception is wrapped by Quartz's own scheduler handling.
- Partial results are possible: an origin can be stored while the job fails on a later image, and an
  embedding can be stored while a later chunk fails.

## Idempotency

Triggers are not idempotent. Firing `createembedding` twice for the same origin creates a **second**
`Embedding` row with its own chunk set (both may be `Active`), which doubles the knowledge blocks in the
system prompt. `recreate` deletes the chunks of the given embedding id first, so it is safe to re-run for
the same embedding. `addfromattachment` writes a new origin each time — origin uniqueness is enforced by
the unique index `(tenant_id, owner_entity, owner_id, name)` (`20260131130711_OriginPayload.cs:56-60`),
so a repeat with the same attachment id and name fails at the database level.

## Related pages

- [knowledge-pipeline.md](knowledge-pipeline.md) — what the jobs do
- [payload-offloading.md](payload-offloading.md) — storage calls made from jobs
- [upstream-dependencies.md](upstream-dependencies.md) — Meta/Storage endpoints used by jobs
