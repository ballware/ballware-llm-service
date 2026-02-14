using Ballware.Llm.Engine.Commons.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Engine.Commons;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmEngineCommons(
        this IServiceCollection services)
    {
        services.AddSingleton<MathToolProvider>();
        
        return services;
    }
}