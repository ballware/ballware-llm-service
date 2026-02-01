using Ballware.Shared.Data.Repository;
using Ballware.Storage.Service.Client;

namespace Ballware.Llm.Service.Extensions;

public class OriginPayloadRepositoryHook(StorageServiceClient storageClient) : ITenantableRepositoryHook<Ballware.Llm.Data.Public.Origin, Ballware.Llm.Data.Persistables.Origin>
{
    public Data.Public.Origin ExtendById(Guid tenantId, string identifier, IDictionary<string, object> claims, Data.Public.Origin value)
    {
        if ("primary".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            var reportBinaryResponse = storageClient.AttachmentDownloadForTenantEntityAndOwnerByFilename(tenantId, "origin", value.Id, CreatePayloadFileName(value.ContentType));

            if (reportBinaryResponse != null)
            {
                using var reader = new StreamReader(stream: reportBinaryResponse.Stream, encoding: System.Text.Encoding.Unicode);
                
                value.Content = reader.ReadToEnd();
            }
        }

        return value;
    }

    public void BeforeSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, Data.Public.Origin value, bool insert)
    {
        if ("primary".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            if (!string.IsNullOrEmpty(value.Content) && userId != null)
            {
                var bytes = System.Text.Encoding.Unicode.GetBytes(value.Content);
                using var stream = new MemoryStream(bytes);
                
                storageClient.AttachmentUploadForTenantEntityAndOwnerBehalfOfUser(tenantId, userId.Value, "origin", value.Id,
                    [new FileParameter(stream, CreatePayloadFileName(value.ContentType), value.ContentType)]);
            }
        }
    }

    public void BeforeRemove(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Data.Persistables.Origin persistable)
    {
        storageClient.AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser(tenantId, userId.Value, "origin", persistable.Uuid, CreatePayloadFileName(persistable.ContentType));
    }

    private static string CreatePayloadFileName(string contentType)
    {
        return $"payload{MimeTypes.MimeTypeMap.GetExtension(contentType)}";
    }
}