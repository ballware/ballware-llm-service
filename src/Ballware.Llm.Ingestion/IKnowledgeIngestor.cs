namespace Ballware.Llm.Ingestion;

public class IngestionResult
{
    public required string Format { get; set; }
    public string Payload { get; set; }
}

public interface IKnowledgeIngestor
{
    Task<IngestionResult> IngestFromStreamAsync(string type, Stream payload);
}