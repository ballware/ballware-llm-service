namespace Ballware.Llm.Ingestion.Docling.Internal;

public class DoclingMarkdownIngestor(IDoclingClient doclingClient ) : IKnowledgeIngestor
{
    public async Task<IngestionResult> IngestFromStreamAsync(string type, Stream payload)
    {
        var ingestionResult = await doclingClient.ConvertToMarkdownWithImagesAsync(type, payload);

        return new IngestionResult()
        {
            Format = "text/markdown",
            Payload = ingestionResult.Markdown,
            Images = ingestionResult.Images
        };
    }
}
