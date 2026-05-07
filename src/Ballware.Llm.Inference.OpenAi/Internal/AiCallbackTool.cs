using System.Text.Json;
using Ballware.Llm.Tools;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Inference.OpenAi.Internal;

sealed class AiCallbackTool(
    string name,
    string description,
    IReadOnlyList<ToolParameter> parameters,
    Func<IDictionary<string, object?>?, Task<object?>> invoke)
    : AIFunction
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var values = arguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return await invoke(values);
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
    
    public override string Name { get; } = name;

    public override string Description { get; } = description;

    public override JsonElement JsonSchema { get; } = BuildJsonSchema(parameters);

    public Task<object?> InvokeMethodAsync(IDictionary<string, object?>? args)
        => invoke(args);
}