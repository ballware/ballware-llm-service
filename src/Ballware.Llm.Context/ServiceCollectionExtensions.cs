using Ballware.Llm.Context.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Context;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmContext(this IServiceCollection services)
    {
        services.AddScoped<IModelContextProvider, ModelContextProvider>();
        
        return services;
    }
}