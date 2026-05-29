using System.Text.Json;
using Ballware.Llm.Tools;
using Microsoft.Extensions.AI;

namespace Ballware.Assistant.Workflow.MsAgentNetwork.Internal;

internal sealed class MsAgentCallbackTool(Tool tool) : AIFunction
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var values = arguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return await tool.Executor(values);
    }

    public override string Name { get; } = tool.Name;

    public override string Description { get; } = tool.Description;

    public override JsonElement JsonSchema { get; } = BuildJsonSchema(tool.Parameters);

    private static JsonElement BuildJsonSchema(IReadOnlyList<ToolParameter> parameters)
    {
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();

        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = new Dictionary<string, object?>
            {
                ["type"] = parameter.Type,
                ["description"] = parameter.Description
            };

            if (parameter.Required)
            {
                required.Add(parameter.Name);
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
}
