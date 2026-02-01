using System.Net.Mime;
using Ballware.Llm.Jobs;
using Ballware.Shared.Api;
using Ballware.Storage.Service.Client;
using Attachment = Ballware.Llm.Metadata.Attachment;

namespace Ballware.Llm.Service.Adapter;

public class StorageServiceFileStorageAdapter : IFileStorageProvider, IJobFileStorageAdapter
{
    private StorageServiceClient StorageClient { get; }
    
    public StorageServiceFileStorageAdapter(StorageServiceClient storageClient)
    {
        StorageClient = storageClient;
    }
    
    public async Task<Stream> TemporaryFileByIdAsync(Guid tenantId, Guid temporaryId)
    {
        return (await StorageClient.TemporaryDownloadForTenantByIdAsync(tenantId, temporaryId)).Stream;
    }

    public async Task UploadTemporaryFileBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId, string fileName,
        string contentType, Stream data)
    {
        await StorageClient.TemporaryUploadForTenantAndIdBehalfOfUserAsync(tenantId, userId, temporaryId, [
            new FileParameter(data, fileName, contentType)
        ]);
    }

    public async Task RemoveTemporaryFileByIdBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId)
    {
        await StorageClient.TemporaryDropForTenantAndIdBehalfOfUserAsync(tenantId, userId, temporaryId);
    }

    public async Task<(string ContentType, Stream? Stream)> AttachmentPayloadByIdForOwnerAsync(Guid tenantId, string ownerEntity, Guid ownerId, Guid attachmentId)
    {
        var response = await StorageClient.AttachmentDownloadForTenantEntityAndOwnerByIdAsync(tenantId, ownerEntity, ownerId,
            attachmentId);
        
        var contentType = response.Headers["Content-Type"].First();
        
        var stream = response.Stream;

        return (contentType, stream);
    }
}