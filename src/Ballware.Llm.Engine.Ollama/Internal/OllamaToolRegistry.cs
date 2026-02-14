using System.Collections.Immutable;
using Ballware.Llm.Engine.Agent;
using OllamaSharp.Models.Chat;

namespace Ballware.Llm.Engine.Ollama.Internal;

class OllamaToolRegistry : IToolRegistry
{
    private readonly List<IToolProvider> _providers = [];

    public void RegisterToolProvider(IToolProvider toolProvider)
    {
        _providers.Add(toolProvider);
    }
    
    internal IEnumerable<OllamaCallbackTool> GetToolsForUser(Guid tenantId, Guid userId, IDictionary<string, object> claims)
    {
        var tools = _providers
            .SelectMany(p => p.GetToolsForUser(tenantId, userId, claims).Select(t => new OllamaCallbackTool(
                t.Name,
                t.Description,
                new Parameters()
                {
                    Type = "object",
                    Properties = t.Parameters.ToDictionary(p => p.Name, p => new Property()
                    {
                        Type = p.Type,
                        Description = p.Description
                    })
                },
                async args => await t.Executor(args ?? ImmutableDictionary<string, object?>.Empty))));
        
        return tools;
    }
}