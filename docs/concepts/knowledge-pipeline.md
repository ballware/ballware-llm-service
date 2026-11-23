# Knowledge pipeline

`document → Docling Serve → markdown + images → origin → chunking → embeddings + vectors`. Every step is
job-driven; the HTTP API only enqueues work.

```text
POST /ai/origin/addfromattachment
   → Quartz job ("addfromattachment","origin")
       Storage Service: read attachment (ownerEntity/ownerId/attachmentId)
       Docling Serve: convert (twice) → markdown + base64 images
       write origin row  (payload → Storage Service, see payload-offloading.md)
       write image rows  (vector + text) + upload image binaries

POST /ai/origin/createembedding
   → Quartz job ("createembedding","origin")
       chunk origin.Content (MarkdownChunker)
       write embedding row
       embed each chunk → chunk rows (vector(768))
       embed extracted-image texts → additional chunk rows

POST /ai/embedding/recreate
   → Quartz job ("recreate","embedding")
       delete old chunks, apply chunk settings, re-chunk, re-embed
```

## 1. Docling conversion

`src/Ballware.Llm.Ingestion.Docling/Internal/DoclingHttpApiClient.cs` drives three Docling Serve
operations (paths relative to `Docling.Url`):

| Step | Call | Detail |
| --- | --- | --- |
| 1 | `POST v1/convert/file/async` | `multipart/form-data`: field `files` with file name `input` and the source content type (`DoclingApiClient.Extensions.cs:20-24`), form field **`output_format`** (`:25-27`), and form field **`image_export_mode`** only when an image mode is requested (`:29-34`). `Accept: application/json` (`:38`). `200` → `TaskStatusResponse.task_id`; `422` → `SwaggerException<HTTPValidationError>`; anything else → `SwaggerException` (`:59-109`) |
| 2 | `GET v1/status/poll/{task_id}` | `wait` is omitted (passed as `null`), `DoclingHttpApiClient.cs:99-116` |
| 3 | `GET v1/result/{task_id}` | response read as a **raw string** (`DoclingApiClient.Extensions.cs:112-178`), then deserialized locally |

The generated NSwag client cannot upload binary for this operation (its generated method types `files`
as `IEnumerable<string>`), which is why `StartFileConversionAsync` hand-rolls the request
(`DoclingApiClient.Extensions.cs:9-110`). The generated `DoclingApiClient.cs` is machine-generated
(15,302 lines, NSwag 14.7.1) from `src/Ballware.Llm.Docling.Client/openapi.json` with
`nswag.json` (`className: DoclingApiClient`, `injectHttpClient: true`,
`exceptionClass: SwaggerException`).

### Polling behavior

`DoclingHttpApiClient.cs:99-116`: an unbounded `while (true)` loop with a **fixed 1000 ms** delay
(`PollingIntervalMs`, `:36`); `"success"` returns, `"failure"` throws
`InvalidOperationException("Conversion failed for task <id>")`, anything else keeps polling. There is no
max-attempts limit, no backoff and no cancellation token. The only bound is the per-request
`HttpClient.Timeout` set from `Docling.RequestTimeoutSeconds` (`Startup.cs:133-137`; default 120 s,
`DoclingOptions.cs:7`), i.e. each individual request is bounded, the loop is not.

### The markdown double conversion

`ConvertToMarkdownWithImagesAsync` (`DoclingHttpApiClient.cs:62-80`) is the path the ingestion job uses.
The same bytes are converted **twice** (the stream is buffered once at `:64`):

1. `output_format=md`, `image_export_mode=placeholder` → the markdown text. Docling Serve inserts its
   own image placeholders into it.
2. `output_format=json`, `image_export_mode=embedded` → a JSON document whose image nodes carry base64
   payloads, used only to extract the image binaries.

The repo **returns the markdown verbatim**: it never rewrites the text, never inserts or removes image
links, and never substitutes Docling's placeholders with the extracted images. So the stored
`Origin.Content` contains Docling's placeholder markers, while the images exist as separate `image`
rows whose binaries are in the Storage Service. `Chunk`/`Image.Text` therefore reference images by
placeholder + a separate labelled text, not by an inline link.

`ConvertToMarkdownAsync` (`:50-60`, `placeholder` mode, single conversion) is declared on `IDoclingClient`
but **never called**. `ConvertToJsonAsync` (`:38-48`) sends no `image_export_mode` at all, so Docling's
default (`embedded`) applies, and `DoclingJsonIngestor` extracts **no** images.

### Result parsing

Local DTOs (`DoclingHttpApiClient.cs:7-32`) read: `document.filename`, `document.md_content`,
`document.json_content` (typed `string?`), `document.html_content`, `document.text_content`,
`document.status`. Missing content throws `"Response does not contain markdown content"` /
`"Response does not contain json content"` (`:44, :56, :69-72`); a missing document throws
`"No result in response"` (`:123`).

> Deviation worth knowing: the committed upstream spec
> (`src/Ballware.Llm.Docling.Client/openapi.json`, Docling Serve **1.14.2**, OpenAPI **3.1.0**) uses the
> form field **`to_formats`** (default `["md"]`) and `image_export_mode` (default `embedded`), puts
> `status` at the **top level** of `ConvertDocumentResponse`, and types `json_content` as an object. The
> repo sends `output_format`, reads `status` from inside `document`, and types `json_content` as a
> string. The repo's WireMock fixtures match the repo's DTOs, not the spec
> (`test/Ballware.Llm.Ingestion.Docling.Tests/DoclingIngestionsTest.cs:189-201, 248-272`), so the
> ingestion path is verified against a mock, not against a real Docling Serve.
> The spec's `APIKeyAuth` (`X-Api-Key`) scheme is **never** used by this repo — no code sets that header.

### Ingestor selection

`Docling.OutputFormat` selects the implementation at registration
(`src/Ballware.Llm.Ingestion.Docling/ServiceCollectionExtensions.cs:16-27`), case-sensitively:

| `OutputFormat` | Ingestor | `IngestionResult.Format` | Images |
| --- | --- | --- | --- |
| `markdown` | `DoclingMarkdownIngestor` | `text/markdown` | extracted |
| `json` | `DoclingJsonIngestor` | `application/json` | **none** |
| anything else | `ConfigurationException("Unsupported output format: …")` | | |

## 2. Image extraction

`src/Ballware.Llm.Ingestion.Docling/Internal/DoclingImageExtractor.cs` walks **every** JSON object at any
depth (`DescendantsAndSelf().OfType<JObject>()`, `:75`) and picks image candidates by key candidates:

| Purpose | Keys (case-insensitive) |
| --- | --- |
| payload | `image_base64`, `base64`, `data`, `binary`, `blob`, `content` (`:10-18`) |
| content type | `content_type`, `contentType`, `mime_type`, `mimetype`, `mime` (`:20-27`) |
| name | `filename`, `file_name`, `name`, `path`, `uri`, `source` (`:29-37`) |
| description | `caption`, `description`, `text`, `title`, `alt` (`:39-46`) |

Rules: content type must start with `image/` (`:123-126`); data URLs are parsed from the payload or from
`uri`/`source` (`:106-135`, `:269-301`); whitespace is stripped from base64 before decoding (`:303-316`);
zero-length payloads are rejected; duplicates are dropped by SHA-256 hash (`:82-86`); missing names become
`image-<n><ext>` (`:88-91`); descriptions fall back to `captions[]` (first three entries joined with a
space, `:166-200`). Any parse failure yields an empty list (`:55-64`).

## 3. Origin and image persistence

`src/Ballware.Llm.Jobs/Internal/OriginAddFromAttachmentJob.cs`:

| Step | Detail |
| --- | --- |
| read attachment | `AttachmentPayloadByIdForOwnerAsync(tenantId, ownerEntity, ownerId, attachmentId)` (`:38`); `null` stream → `Exception("Attachment could not be found")` (`:40-43`) |
| ingest | `ingestor.IngestFromStreamAsync(contentType, stream)` (`:45`) |
| write origin | `Id = Guid.NewGuid()`, `ReferingEntity = ownerEntity`, `ReferingId = ownerId`, `OwnerEntity = "attachment"`, `OwnerId = attachmentId`, `Name = name`, `Description = "None"`, `ContentType = ingestionResult.Format`, `Content = ingestionResult.Payload`; saved with identifier `primary` (`:47-60`) |
| write images | per image with a non-empty payload: embedding over a generated description text, then an `Image` row (`Name` fallback `"image"`, `ContentType` fallback `"application/octet-stream"`, `Text` = the description text, `Data` = vector) saved with `primary`, and the binary uploaded to the Storage Service (`:62-85`) |

The image description text that gets embedded is built from German labels joined by newlines
(`BuildEmbeddingText`, `:88-111`): `Dokument: <origin name>`, `Bilddatei: <image name>`,
`Beschreibung: <description>`, `Quelle: <source>`. That same text is stored in `image.text`.

`Origin.GlobalContext` is **not** set by the job — a freshly ingested origin is never part of the system
prompt until it is edited (`GlobalContext: true`) via `POST /ai/origin/save`.

## 4. Chunking and embedding

`src/Ballware.Llm.Jobs/Internal/OriginCreateEmbeddingJob.cs`:

1. `origin = originRepository.ByIdAsync(...)`; `null` → `ArgumentNullException` (`:46-51`).
2. `chunkerType = chunkerRegistry.GetChunker(origin.ContentType)` — the registry only knows
   `"text/markdown"` (`src/Ballware.Llm.Embedding.SemanticKernel/ServiceCollectionExtensions.cs:17`), and
   `ChunkerRegistry.GetChunker` throws `KeyNotFoundException` for anything else
   (`src/Ballware.Llm.Embedding/ChunkerRegistry.cs:15-18`), so the job's own null guard (`:53-58`) is
   unreachable: an origin whose `content_type` is `application/json` fails the job.
3. `chunker.Chunk(origin.Content, new ChunkRequest(origin.Id, maxTokensPerChunk, overlapTokens))` — the
   `Chunking` defaults (500 / 50) are applied **inside** `MarkdownChunker`
   (`src/Ballware.Llm.Embedding.SemanticKernel/Internal/MarkdownChunker.cs:13-20`), which runs
   `TextChunker.SplitMarkDownLines` then `SplitMarkdownParagraphs` with overlap, drops empty chunks and
   tags each chunk with `sourceType`, `chunkIndex`, `originId`.
4. `Embedding` row: `Id`, `OriginId`, `Model = model`, `Active = activate` (`:64-73`). `TopK`,
   `MaxTokensPerChunk` and `OverlapTokens` are **not** written.
5. Per chunk: `GetEmbeddingAsync(chunk.Text, model)` → `Chunk` row (`EmbeddingId`, `Text`, `Data`) (`:77-87`).
6. Extra chunks for images with non-blank `image.Text`, using the text
   `[Bildinhalt] Name: <name>\nBeschreibung: <description>\n<image.Text>` (`:90-106`, `:109-112`) — these
   are normal chunk rows attached to the same embedding, so image content participates in the same
   top-k retrieval.

`src/Ballware.Llm.Jobs/Internal/EmbeddingRecreateJob.cs` differs: it deletes the embedding's chunks
(`:49`), chunks with `embedding.MaxTokensPerChunk ?? 500` and `embedding.OverlapTokens ?? 50` (`:53`),
**writes those effective values back onto the embedding** (`:55-59`), then re-embeds chunks and image
texts. Recreate does not re-run Docling — it re-reads `Origin.Content` from the Storage Service via the
by-id hook.

## 5. Dimension contract

Vectors are written into `vector(768)` columns; nothing validates the length of what the embedding
endpoint returns. If the configured `Embedding.Model` produces a different dimensionality, inserts fail
(see [data-model.md](data-model.md)). Image embeddings are requested with `model: null`
(`OriginAddFromAttachmentJob.cs:67`), i.e. they always use the configured `Embedding:Model`, while chunk
embeddings use the request's `Model` (`OriginCreateEmbeddingJob.cs:77`) — mixing them into one embedding
set can therefore mix dimensions.

## Related pages

- [background-jobs.md](background-jobs.md) — job keys, payloads, error handling
- [rag-context-and-tools.md](rag-context-and-tools.md) — how the produced vectors are used
- [data-model.md](data-model.md) — where the results are stored
- [upstream-dependencies.md](upstream-dependencies.md) — the Docling Serve spec in this repo
