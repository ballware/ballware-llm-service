using Ballware.Shared.Data.Repository;
using Ballware.Storage.Service.Client;

namespace Ballware.Llm.Service.Extensions;

public class ImagePayloadRepositoryHook(StorageServiceClient storageClient) : ITenantableRepositoryHook<Ballware.Llm.Data.Public.Image, Ballware.Llm.Data.Persistables.Image>
{
    public Data.Public.Image ExtendById(Guid tenantId, string identifier, IDictionary<string, object> claims, Data.Public.Image value)
    {
        return value;
    }

    public void BeforeSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, Data.Public.Image value, bool insert)
    {
    }

    public void BeforeRemove(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Data.Persistables.Image persistable)
    {
        if (!userId.HasValue)
        {
            return;
        }

        storageClient.AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser(
            tenantId,
            userId.Value,
            "image",
            persistable.Uuid,
            CreatePayloadFileName(persistable.ContentType));
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
