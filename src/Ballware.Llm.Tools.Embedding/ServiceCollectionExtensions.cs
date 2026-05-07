using Ballware.Llm.Tools.Embedding.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Tools.Embedding;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmEmbeddingTools(
        this IServiceCollection services)
    {
        services.AddScoped<EmbeddingsToolProvider>();
        
        return services;
    }
}