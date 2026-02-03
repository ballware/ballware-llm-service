namespace Ballware.Llm.Ingestion.Docling.Configuration;

public class DoclingOptions
{
    public required string Url { get; set; }
    public required string OutputFormat { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 120;
}