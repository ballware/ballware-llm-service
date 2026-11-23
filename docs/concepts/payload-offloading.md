# Payload offloading

Document text and image binaries are **not** stored in the LLM database. They are attachments in the
ballware **Storage Service**; the LLM tables keep only metadata plus, for images, the embedding vector.

## Attachment addressing

Attachments are addressed by a triple, not by a free-form identifier:

| Part | Value | Source |
| --- | --- | --- |
| owner entity | `"origin"` for document text, `"image"` for image binaries | `src/Ballware.Llm.Hosting/Extensions/OriginPayloadRepositoryHook.cs:12, 34, 42`, `…/Adapter/StorageServiceFileStorageAdapter.cs:47` |
| owner id | the row's `uuid` (origin uuid / image uuid) | same |
| file name | `payload<extension>` — extension derived from the content type | `OriginPayloadRepositoryHook.cs:45-62`, `ImagePayloadRepositoryHook.cs:32-42`, `StorageServiceFileStorageAdapter.cs:52-62` |

There is no separate "attachment identifier" string in this service — the query identifier `"primary"`
gates the hook (see below), and the file name is always `payload` + extension. Examples:
`payload.md` for a `text/markdown` origin, `payload.png` for a `image/png` image, `payload.bin` for an
unrecognized image content type.

Extension resolution uses `MimeTypes.MimeTypeMap.GetExtension(contentType, false)`:

| Case | Origin hook | Image/storage adapters |
| --- | --- | --- |
| known content type | `payload<ext>` | `payload.<ext>` (a missing leading dot is added) |
| `text/markdown` with an unmappable type | `.md` (`OriginPayloadRepositoryHook.cs:51-54`) | — |
| unknown content type | **throws** `InvalidOperationException("Unsupported content type: <type>")` (`:57`) | falls back to `.bin` (`ImagePayloadRepositoryHook.cs:38`, `StorageServiceFileStorageAdapter.cs:58`) |

## Origin text

`src/Ballware.Llm.Hosting/Extensions/OriginPayloadRepositoryHook.cs`, registered as
`ITenantableRepositoryHook<Public.Origin, Persistables.Origin>`
(`src/Ballware.Llm.Hosting/LlmHostingExtensions.cs:270`).

| Hook | When | What happens |
| --- | --- | --- |
| `BeforeSave` (`:25-38`) | inside `SaveAsync`, before insert/update (`TenantableBaseRepository.cs:163`) | only when the operation's query identifier is `primary` **and** `Content` is non-empty **and** a user id is available: `Encoding.Unicode.GetBytes(Content)` → `AttachmentUploadForTenantEntityAndOwnerBehalfOfUser(tenantId, userId, "origin", origin.Uuid, [FileParameter(stream, "payload<ext>", ContentType)])` |
| `ExtendById` (`:8-23`) | inside `ByIdAsync` (`TenantableBaseRepository.cs:132`) and `ExportAsync` (`:258`) | `AttachmentDownloadForTenantEntityAndOwnerByFilename(tenantId, "origin", origin.Uuid, "payload<ext>")` → read with `StreamReader(Encoding.Unicode)` → assigned to `Origin.Content`; when the response is null, `Content` stays empty |
| `BeforeRemove` (`:40-43`) | inside `RemoveAsync` (`TenantableBaseRepository.cs:220`) | `AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser(...)` — uses `userId.Value` **without a null check**, so removing an origin without a user context throws |

Consequences for API consumers:

- `POST /ai/origin/save` with `Content` writes the payload to the Storage Service; the LLM database
  stores only `content_type`.
- `GET /ai/origin/byid` and `export` return `Content`; `GET /ai/origin/all` and `/query` return it empty
  (the hook is not invoked).
- `OriginSaveBatch` runs the same hook per item.
- Saving with an identifier other than `primary` does **not** upload or read the payload
  (`:10, :27`).
- Encoding is `Encoding.Unicode` (UTF-16 LE) on both write and read, so stored bytes are UTF-16, not
  UTF-8. Reading such a file outside this service requires the same encoding.

## Image binaries

Upload happens in the job, not in a hook:
`OriginAddFromAttachmentJob.cs:82-83` calls
`IJobFileStorageAdapter.UploadImagePayloadByIdForOwnerBehalfOfUserAsync(tenantId, userId, image.Id,
image.ContentType, imagePayload)`, implemented by
`StorageServiceFileStorageAdapter.cs:45-50` →
`AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync(tenantId, userId, "image", imageId, [FileParameter(payload, "payload<ext>", contentType)])`.

`ImagePayloadRepositoryHook.cs`:

| Hook | Behavior |
| --- | --- |
| `ExtendById` (`:8-11`) | **no-op** — `GET /ai/image/byid` never returns the binary |
| `BeforeSave` (`:13-15`) | **empty** — image payloads are uploaded explicitly by the job |
| `BeforeRemove` (`:17-30`) | drops the attachment for owner entity `"image"`, but returns early when no user id is available (`:19-22`) |

The `Image` schema exposes `Text` (the embedded description) and `Data` (vector) only. No endpoint
serves an image's binary; a client that needs it must call the Storage Service directly for
entity `image` / owner `image.uuid` / file `payload<ext>`.

## Temporary files (import and export flows)

The same adapter also implements `Ballware.Shared.Api.IFileStorageProvider`
(`StorageServiceFileStorageAdapter.cs:15-31`) for the generic editing flows:

| Method | Storage Service call | Used by |
| --- | --- | --- |
| `TemporaryFileByIdAsync` | `TemporaryDownloadForTenantByIdAsync` | the `import` job reading the uploaded file |
| `UploadTemporaryFileBehalfOfUserAsync` | `TemporaryUploadForTenantAndIdBehalfOfUserAsync` | the `import` handler staging the uploaded file |
| `RemoveTemporaryFileByIdBehalfOfUserAsync` | `TemporaryDropForTenantAndIdBehalfOfUserAsync` | cleanup after import |
| `AttachmentPayloadByIdForOwnerAsync` | `AttachmentDownloadForTenantEntityAndOwnerByIdAsync` | `addfromattachment` reading the source document; content type is taken from the **response header** `Content-Type` (`:38`) |

`IFileStorageProvider` is registered in Hosting (`LlmHostingExtensions.cs:268`); `IJobFileStorageAdapter`
is registered by the Service host only (`src/Ballware.Llm.Service/Startup.cs:159` through
`Adapter/StorageServiceJobFileStorageAdapter.cs`, a subclass that adds no members). The App host has
neither, because it registers no jobs.

## Authentication of the outbound calls

The typed `HttpClient<StorageServiceClient>` uses `BaseAddress = StorageClient.ServiceUrl` and, when
`StorageClient.CredentialIdentifier` is set, a Duende client-credentials token handler
(`LlmHostingExtensions.cs:185-200`). Credentials come from `ServiceCredentials.Credentials[]`
(`Identifier`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scopes` —
`StorageServiceFileStorageAdapter`'s counterpart `ServiceClientCredential.cs:5-20`). The calls are made
**on behalf of** the tenant/user taken from the incoming token's claims.

## Failure behavior

None of these hooks or adapters catch anything: a Storage Service outage, a missing attachment, or an
unmappable content type in the origin hook propagates as an unhandled exception → `500`
`application/problem+json` (`src/Ballware.Llm.Service/Startup.cs:211-236`). Inside a job it aborts the
job.

Note also that the origin hook calls the **synchronous** client variants
(`AttachmentDownloadForTenantEntityAndOwnerByFilename` at `:12`,
`AttachmentUploadForTenantEntityAndOwnerBehalfOfUser` at `:34-35`,
`AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser` at `:42`) — no `await`, so these run as
blocking calls inside the async request pipeline.

## Related pages

- [data-model.md](data-model.md) — why `origin` has no content column
- [background-jobs.md](background-jobs.md) — the jobs that read/write attachments
- [upstream-dependencies.md](upstream-dependencies.md) — the Storage Service contract
