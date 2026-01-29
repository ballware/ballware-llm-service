using Ballware.Llm.Api.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmApiDependencies(this IServiceCollection services)
    {
        services.AddSingleton<ChatSessionStore>();
        
        return services;
    }
}