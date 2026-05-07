using Ballware.Llm.Embedding.SemanticKernel.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Embedding.SemanticKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareSemanticKernelChunker(this IServiceCollection services)
    {
        services.AddScoped<MarkdownChunker>();
        
        return services;
    }
    
    public static ChunkerRegistry RegisterSemanticKernelChunker(this ChunkerRegistry registry)
    {
        registry.AddChunker("text/markdown", typeof(MarkdownChunker));
    
        return registry;
    }
}