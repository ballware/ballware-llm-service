using Ballware.Llm.Tools.Common.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Tools.Common;

public static class ToolRegistryExtensions
{
    public static IToolRegistry RegisterBallwareLlmCommonTools(
        this IToolRegistry registry, IServiceProvider serviceProvider)
    {
        registry.RegisterStaticToolProvider(serviceProvider.GetRequiredService<MathToolProvider>());
        
        return registry;
    }
}
