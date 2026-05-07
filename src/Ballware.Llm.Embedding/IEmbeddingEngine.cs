namespace Ballware.Llm.Embedding;

public interface IEmbeddingEngine
{ 
    Task<float[]> GetEmbeddingAsync(string chunk, string? model = null, CancellationToken cancellationToken = default);
}