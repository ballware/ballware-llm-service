using Ballware.Llm.Context.Configuration;
using Ballware.Llm.Context.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Context;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmContext(this IServiceCollection services, ContextOptions options)
    {
        services.AddSingleton<ContextOptions>(options);
        services.AddScoped<IModelContextProvider, ModelContextProvider>();
        
        return services;
    }
}