using Ballware.Llm.Hosting.Adapter;
using Ballware.Llm.Jobs;
using Ballware.Storage.Service.Client;

namespace Ballware.Llm.Service.Adapter;

public class StorageServiceJobFileStorageAdapter(StorageServiceClient storageClient)
    : StorageServiceFileStorageAdapter(storageClient), IJobFileStorageAdapter
{
}
