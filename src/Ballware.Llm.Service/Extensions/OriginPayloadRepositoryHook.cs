using Ballware.Shared.Data.Repository;
using Ballware.Storage.Service.Client;

namespace Ballware.Llm.Service.Extensions;

public class OriginPayloadRepositoryHook(StorageServiceClient storageClient) : ITenantableRepositoryHook<Ballware.Llm.Data.Public.Origin, Ballware.Llm.Data.Persistables.Origin>
{
    public Data.Public.Origin ExtendById(Guid tenantId, string identifier, IDictionary<string, object> claims, Data.Public.Origin value)
    {
        if ("primary".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            var reportBinaryResponse = storageClient.AttachmentDownloadForTenantEntityAndOwnerByFilename(tenantId, "origin", value.Id, "payload.txt");

            if (reportBinaryResponse != null)
            {
                using var reader = new StreamReader(stream: reportBinaryResponse.Stream, encoding: System.Text.Encoding.Unicode);
                
                value.Payload = reader.ReadToEnd();
            }
        }

        return value;
    }

    public void BeforeSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, Data.Public.Origin value, bool insert)
    {
        if ("primary".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            if (!string.IsNullOrEmpty(value.Payload) && userId != null)
            {
                var bytes = System.Text.Encoding.Unicode.GetBytes(value.Payload);
                using var stream = new MemoryStream(bytes);
                
                storageClient.AttachmentUploadForTenantEntityAndOwnerBehalfOfUser(tenantId, userId.Value, "origin", value.Id,
                    [new FileParameter(stream, "payload.txt", "text/plain")]);
            }
        }
    }

    public void BeforeRemove(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Data.Persistables.Origin persistable)
    {
        storageClient.AttachmentDropForTenantEntityAndOwnerByFilenameBehalfOfUser(tenantId, userId.Value, "origin", persistable.Uuid, "payload.txt");
    }
}