using Ballware.Llm.Inference.OpenAi.Configuration;
using Ballware.Llm.Inference.OpenAi.Internal;
using Ballware.Llm.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballware.Llm.Inference.OpenAi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareInferenceEngineForOpenAi(
        this IServiceCollection services, 
        OpenAiInferenceOptions options,
        Action<IServiceProvider, IToolRegistry>? registerTools = null)
    {
        var toolRegistry = new DefaultToolRegistry();

        services.AddSingleton(options);
        services.AddSingleton(toolRegistry);
        services.AddSingleton<IToolRegistry>(toolRegistry);
        services.AddSingleton<IInferenceEngine, AiInferenceEngine>();

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