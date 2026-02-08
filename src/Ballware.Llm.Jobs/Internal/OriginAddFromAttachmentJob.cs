using System.Collections.Immutable;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Ingestion;
using Quartz;

namespace Ballware.Llm.Jobs.Internal;

public class OriginAddFromAttachmentJob(IJobFileStorageAdapter storageAdapter, IOriginRepository originRepository, IKnowledgeIngestor ingestor) : IJob
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
    }
}