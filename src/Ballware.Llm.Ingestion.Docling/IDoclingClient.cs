namespace Ballware.Llm.Ingestion.Docling;

public interface IDoclingClient
{
    Task<string> ConvertToJsonAsync(string type, Stream input);
}