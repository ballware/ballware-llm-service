using Ballware.Llm.Embedding.OpenAi.Configuration;
using Ballware.Llm.Embedding.OpenAi.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Embedding.OpenAi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareEmbeddingOpenAi(this IServiceCollection services, OpenAiEmbeddingOptions options)
    {
        services.AddSingleton(options);
        services.AddScoped<IEmbeddingEngine, OpenAiEmbeddingEngine>();
        
        return services;
    }
}