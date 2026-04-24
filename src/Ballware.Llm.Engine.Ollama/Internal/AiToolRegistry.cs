using System.Collections.Immutable;
using Ballware.Llm.Engine.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Engine.Ollama.Internal;

class AiToolRegistry : IToolRegistry
{
    private readonly List<IMcpService> _services = [];
    private readonly List<IToolProvider> _staticProviders = [];
    private readonly List<Type> _dynamicProviders = [];

    public void RegisterStaticToolProvider(IToolProvider toolProvider)
    {
        _staticProviders.Add(toolProvider);
    }

    public void RegisterScopedToolProvider<T>() where T : IToolProvider
    {
        _dynamicProviders.Add(typeof(T));
    }

    public void RegisterMcpService(IMcpService mcpService)
    {
        _services.Add(mcpService);
    }

    internal IEnumerable<IMcpService> GetMcpServices() => _services;
    
    internal IEnumerable<AITool> GetToolsForUser(IServiceProvider scopeServiceProvider, Guid tenantId, Guid userId, IDictionary<string, object> claims)
    {
        var tools = _staticProviders
            .SelectMany(p => p.GetToolsForUser(tenantId, userId, claims).Select(t => new AiCallbackTool(
                t.Name,
                t.Description,
                t.Parameters,
                async args => await t.Executor(args ?? ImmutableDictionary<string, object?>.Empty))))
            .ToList();
        
        tools.AddRange(_dynamicProviders.SelectMany(p =>
        {
            var provider = (IToolProvider)scopeServiceProvider.GetRequiredService(p);
            
            return provider.GetToolsForUser(tenantId, userId, claims).Select(t => new AiCallbackTool(
                t.Name,
                t.Description,
                t.Parameters,
                async args => await t.Executor(args ?? ImmutableDictionary<string, object?>.Empty)));
        }));
        
        return tools;
    }
}