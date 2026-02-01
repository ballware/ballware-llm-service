namespace Ballware.Llm.Ingestion.Docling.Internal;

public class DoclingJsonIngestor(IDoclingClient doclingClient ) : IKnowledgeIngestor
{
    public async Task<IngestionResult> IngestFromStreamAsync(string type, Stream payload)
    {
        var ingestionResult = await doclingClient.ConvertToJsonAsync(type, payload);

        return new IngestionResult()
        {
            Format = "application/json",
            Payload = ingestionResult,
        };
    }
}