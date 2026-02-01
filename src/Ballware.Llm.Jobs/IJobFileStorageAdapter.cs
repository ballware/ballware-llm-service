using Ballware.Llm.Metadata;

namespace Ballware.Llm.Jobs;

public interface IJobFileStorageAdapter
{
    Task<(string ContentType, Stream? Stream)> AttachmentPayloadByIdForOwnerAsync(Guid tenantId, string ownerEntity, Guid ownerId, Guid attachmentId);
}