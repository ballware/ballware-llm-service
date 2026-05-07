using Ballware.Llm.Embedding.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Embedding;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareEmbeddingChunker(this IServiceCollection services, ChunkingOptions options, Action<ChunkerRegistry>? configure = null)
    {
        services.AddSingleton(options);
        
        var chunkerRegistry = new ChunkerRegistry();
        
        services.AddSingleton(chunkerRegistry);
        
        configure?.Invoke(chunkerRegistry);
        
        return services;
    }
}