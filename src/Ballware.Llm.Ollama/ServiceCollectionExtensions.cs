using Ballware.Llm.Ollama.Configuration;
using Ballware.Llm.Ollama.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareOllamaModelBridge(this IServiceCollection services, OllamaOptions options)
    {
        services.AddSingleton<IOllamaModelBridge>(new OllamaModelBridge(options));
        
        return services;
    }
}