using System.Collections.Immutable;
using Ballware.Llm.Engine.Agent;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Engine.Ollama.Internal;

class AiToolRegistry : IToolRegistry
{
    private readonly List<IMcpService> _services = [];
    private readonly List<IToolProvider> _providers = [];

    public void RegisterToolProvider(IToolProvider toolProvider)
    {
        _providers.Add(toolProvider);
    }

    public void RegisterMcpService(IMcpService mcpService)
    {
        _services.Add(mcpService);
    }

    internal IEnumerable<IMcpService> GetMcpServices() => _services;
    
    internal IEnumerable<AITool> GetToolsForUser(Guid tenantId, Guid userId, IDictionary<string, object> claims)
    {
        var tools = _providers
            .SelectMany(p => p.GetToolsForUser(tenantId, userId, claims).Select(t => new AiCallbackTool(
                t.Name,
                t.Description,
                t.Parameters,
                async args => await t.Executor(args ?? ImmutableDictionary<string, object?>.Empty))));
        
        return tools;
    }
}