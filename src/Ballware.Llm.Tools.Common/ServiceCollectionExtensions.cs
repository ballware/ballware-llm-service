using Ballware.Llm.Tools.Common.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Tools.Common;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmCommonTools(
        this IServiceCollection services)
    {
        services.AddSingleton<MathToolProvider>();
        
        return services;
    }
}