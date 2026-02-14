using Ballware.Llm.Engine.Agent;
using Ballware.Llm.Engine.Ollama.Configuration;
using Ballware.Llm.Engine.Ollama.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballware.Llm.Engine.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareOllamaEngine(
        this IServiceCollection services, 
        OllamaOptions options,
        Action<IServiceProvider, IToolRegistry>? registerTools = null)
    {
        var toolRegistry = new OllamaToolRegistry();

        services.AddSingleton(options);
        services.AddSingleton(toolRegistry);
        services.AddSingleton<IToolRegistry>(toolRegistry);
        services.AddSingleton<IOllamaModelBridge, OllamaModelBridge>();

        if (registerTools != null)
        {
            services.AddHostedService(sp => new OllamaToolRegistryInitializer(sp, registerTools));
        }
        
        return services;
    }
    
    private class OllamaToolRegistryInitializer(
        IServiceProvider serviceProvider, 
        Action<IServiceProvider, IToolRegistry> registerTools) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
            registerTools(serviceProvider, toolRegistry);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}