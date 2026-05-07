using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Embedding;

public class ChunkerRegistry
{
    private readonly Dictionary<string, Type> _chunkers = new();
    
    public void AddChunker(string contentType, Type chunkerType)
    {
        _chunkers.Add(contentType, chunkerType);
        
    }
    
    public Type GetChunker(string contentType)
    {
        return _chunkers[contentType];
    }
}