using System.Collections.Immutable;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Embedding;
using Ballware.Llm.Ingestion;
using Quartz;

namespace Ballware.Llm.Jobs.Internal;

public class OriginAddFromAttachmentJob(
    IJobFileStorageAdapter storageAdapter,
    IOriginRepository originRepository,
    IImageRepository imageRepository,
    IEmbeddingEngine embeddingEngine,
    IKnowledgeIngestor ingestor) : IJob
{
    public static readonly JobKey Key = new JobKey("addfromattachment", "origin");

    public async Task Execute(IJobExecutionContext context)
    {
        var tenantId = context.MergedJobDataMap.GetGuidValue("tenantId");
        var userId = context.MergedJobDataMap.GetGuidValue("userId");
        var ownerEntity = context.MergedJobDataMap.GetString("ownerEntity");
        var ownerId = context.MergedJobDataMap.GetGuidValue("ownerId");
        var attachmentId = context.MergedJobDataMap.GetGuidValue("attachmentId");
        var name = context.MergedJobDataMap.GetString("name");

        if (ownerEntity == null)
        {
            throw new ArgumentNullException(nameof(ownerEntity));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        var attachmentResponse = await storageAdapter.AttachmentPayloadByIdForOwnerAsync(tenantId, ownerEntity, ownerId, attachmentId);

        if (attachmentResponse.Stream == null)
        {
            throw new Exception("Attachment could not be found");
        }
        
        var ingestionResult = await ingestor.IngestFromStreamAsync(attachmentResponse.ContentType, attachmentResponse.Stream);
        
        var origin = new Origin()
        {
            Id = Guid.NewGuid(),
            ReferingEntity = ownerEntity,
            ReferingId = ownerId,
            OwnerEntity = "attachment",
            OwnerId = attachmentId,
            Name = name,
            Description = "None",
            ContentType = ingestionResult.Format,
            Content = ingestionResult.Payload,
        };

        await originRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty, origin);

        if (ingestionResult.Images.Count > 0)
        {
            foreach (var extractedImage in ingestionResult.Images.Where(image => image.Payload.Length > 0))
            {
                var embeddingText = BuildEmbeddingText(extractedImage, origin.Name);
                var imageEmbedding = await embeddingEngine.GetEmbeddingAsync(embeddingText, null);

                var image = new Data.Public.Image()
                {
                    Id = Guid.NewGuid(),
                    OriginId = origin.Id,
                    Name = string.IsNullOrWhiteSpace(extractedImage.Name) ? "image" : extractedImage.Name,
                    Description = extractedImage.Description ?? string.Empty,
                    ContentType = string.IsNullOrWhiteSpace(extractedImage.ContentType) ? "application/octet-stream" : extractedImage.ContentType,
                    Text = embeddingText,
                    Data = imageEmbedding.ToArray()
                };

                await imageRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty, image);

                using var imagePayload = new MemoryStream(extractedImage.Payload, writable: false);
                await storageAdapter.UploadImagePayloadByIdForOwnerBehalfOfUserAsync(tenantId, userId, image.Id, image.ContentType, imagePayload);
            }
        }
    }

    private static string BuildEmbeddingText(IngestionImage image, string originName)
    {
        var parts = new List<string>
        {
            $"Dokument: {originName}"
        };

        if (!string.IsNullOrWhiteSpace(image.Name))
        {
            parts.Add($"Bilddatei: {image.Name}");
        }

        if (!string.IsNullOrWhiteSpace(image.Description))
        {
            parts.Add($"Beschreibung: {image.Description}");
        }

        if (!string.IsNullOrWhiteSpace(image.Source))
        {
            parts.Add($"Quelle: {image.Source}");
        }

        return string.Join(Environment.NewLine, parts);
    }
}
