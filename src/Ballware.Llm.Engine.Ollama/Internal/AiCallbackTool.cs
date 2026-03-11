using System.Text.Json;
using Ballware.Llm.Engine.Agent;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Engine.Ollama.Internal;

sealed class AiCallbackTool : AIFunction
{
    private readonly Func<IDictionary<string, object?>?, Task<object?>> _invoke;

    public AiCallbackTool(
        string name,
        string description,
        IReadOnlyList<ToolParameter> parameters,
        Func<IDictionary<string, object?>?, Task<object?>> invoke)
    {
        Name = name;
        Description = description;
        JsonSchema = BuildJsonSchema(parameters);
        
        _invoke = invoke;
    }
    
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var values = arguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return await _invoke(values);
    }
    
    private static JsonElement BuildJsonSchema(IReadOnlyList<ToolParameter> parameters)
    {
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();

        foreach (var p in parameters)
        {
            properties[p.Name] = new Dictionary<string, object?>
            {
                ["type"] = p.Type,
                ["description"] = p.Description
            };

            if (p.Required)
            {
                required.Add(p.Name);
            }
        }

        var schemaObject = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
        {
            schemaObject["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schemaObject);
    }
    
    public override string Name { get; }

    public override string Description { get; }
    
    public override JsonElement JsonSchema { get; }

    public Task<object?> InvokeMethodAsync(IDictionary<string, object?>? args)
        => _invoke(args);
}