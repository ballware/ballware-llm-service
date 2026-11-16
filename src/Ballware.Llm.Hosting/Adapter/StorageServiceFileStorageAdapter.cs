using Ballware.Shared.Api;
using Ballware.Storage.Service.Client;

namespace Ballware.Llm.Hosting.Adapter;

public class StorageServiceFileStorageAdapter : IFileStorageProvider
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

    public async Task UploadImagePayloadByIdForOwnerBehalfOfUserAsync(Guid tenantId, Guid userId, Guid imageId, string contentType, Stream payload)
    {
        await StorageClient.AttachmentUploadForTenantEntityAndOwnerBehalfOfUserAsync(tenantId, userId, "image", imageId, [
            new FileParameter(payload, CreatePayloadFileName(contentType), contentType)
        ]);
    }

    private static string CreatePayloadFileName(string contentType)
    {
        var evaluatedExtension = MimeTypes.MimeTypeMap.GetExtension(contentType, false);

        if (string.IsNullOrEmpty(evaluatedExtension))
        {
            evaluatedExtension = ".bin";
        }

        return $"payload{(evaluatedExtension.StartsWith('.') ? evaluatedExtension : $".{evaluatedExtension}")}";
    }
}
