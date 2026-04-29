using Ballware.Llm.Ingestion;

namespace Ballware.Llm.Ingestion.Docling;

public interface IDoclingClient
{
    Task<string> ConvertToJsonAsync(string type, Stream input);
    Task<string> ConvertToMarkdownAsync(string type, Stream input);
    Task<(string Markdown, IReadOnlyList<IngestionImage> Images)> ConvertToMarkdownWithImagesAsync(string type, Stream input);
}
