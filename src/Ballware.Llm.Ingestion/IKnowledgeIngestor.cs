namespace Ballware.Llm.Ingestion;

public class IngestionImage
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Source { get; set; }
    public byte[] Payload { get; set; } = [];
}

public class IngestionResult
{
    public required string Format { get; set; }
    public string Payload { get; set; } = string.Empty;
    public IReadOnlyList<IngestionImage> Images { get; set; } = [];
}

public interface IKnowledgeIngestor
{
    Task<IngestionResult> IngestFromStreamAsync(string type, Stream payload);
}
