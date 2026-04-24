using Ballware.Llm.Engine.Agent;
using Ballware.Llm.Engine.Commons.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Engine.Commons;

public static class ToolRegistryExtensions
{
    public static IToolRegistry RegisterBallwareLlmCommonTools(
        this IToolRegistry registry, IServiceProvider serviceProvider)
    {
        registry.RegisterStaticToolProvider(serviceProvider.GetRequiredService<MathToolProvider>());
        registry.RegisterScopedToolProvider<EmbeddingsToolProvider>();
        
        return registry;
    }
}
