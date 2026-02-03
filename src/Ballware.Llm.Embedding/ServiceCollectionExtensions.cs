using System.Configuration;
using Ballware.Llm.Embedding.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Embedding;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareEmbeddingChunker(this IServiceCollection services)
    {
        var chunkerRegistry = new ChunkerRegistry();
        
        chunkerRegistry.AddChunker("text/markdown", typeof(MarkdownChunker));
        
        services.AddSingleton(chunkerRegistry);
        services.AddScoped<MarkdownChunker>();
        
        return services;
    }
}