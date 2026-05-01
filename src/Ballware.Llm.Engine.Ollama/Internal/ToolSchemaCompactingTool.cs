using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal sealed class ToolSchemaCompactingTool : AIFunction
{
    private static readonly JsonElement FallbackSchema = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["additionalProperties"] = true
    });

    private readonly AITool _inner;

    private ToolSchemaCompactingTool(AITool inner)
    {
        _inner = inner;
        Name = TryReadPropertyAsString(inner, "Name") ?? inner.GetType().Name;
        Description = TryReadPropertyAsString(inner, "Description") ?? string.Empty;
        JsonSchema = BuildCompactInputSchema(inner);
    }

    public static ToolSchemaCompactingTool Create(AITool inner)
    {
        if (inner is ToolSchemaCompactingTool compact)
        {
            return compact;
        }

        return new ToolSchemaCompactingTool(inner);
    }

    public override string Name { get; }

    public override string Description { get; }

    public override JsonElement JsonSchema { get; }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (_inner is AIFunction function)
        {
            return await function.InvokeAsync(arguments, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Tool '{Name}' is not invokable because it does not implement {nameof(AIFunction)}.");
    }

    private static JsonElement BuildCompactInputSchema(AITool tool)
    {
        var source = TryReadSchema(tool);
        if (source is not JsonElement sourceElement || sourceElement.ValueKind != JsonValueKind.Object)
        {
            return FallbackSchema;
        }

        if (!sourceElement.TryGetProperty("properties", out var sourceProperties) ||
            sourceProperties.ValueKind != JsonValueKind.Object)
        {
            return FallbackSchema;
        }

        var requiredNames = ReadRequiredNames(sourceElement);
        var compactProperties = new Dictionary<string, object?>();
        var compactRequired = new List<string>();

        foreach (var property in sourceProperties.EnumerateObject())
        {
            compactProperties[property.Name] = CompactProperty(property.Value);

            if (requiredNames.Contains(property.Name))
            {
                compactRequired.Add(property.Name);
            }
        }

        if (compactProperties.Count == 0)
        {
            return FallbackSchema;
        }

        var schemaObject = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = compactProperties,
            ["additionalProperties"] = false
        };

        if (compactRequired.Count > 0)
        {
            schemaObject["required"] = compactRequired;
        }

        return JsonSerializer.SerializeToElement(schemaObject);
    }

    private static HashSet<string> ReadRequiredNames(JsonElement schema)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (!schema.TryGetProperty("required", out var requiredElement) || requiredElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var requiredEntry in requiredElement.EnumerateArray())
        {
            if (requiredEntry.ValueKind == JsonValueKind.String)
            {
                var name = requiredEntry.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }
        }

        return result;
    }

    private static Dictionary<string, object?> CompactProperty(JsonElement property)
    {
        var result = new Dictionary<string, object?>();
        var type = ResolveType(property);
        result["type"] = type;

        if ("array".Equals(type, StringComparison.Ordinal))
        {
            if (property.TryGetProperty("items", out var items))
            {
                result["items"] = new Dictionary<string, object?>
                {
                    ["type"] = ResolveType(items)
                };
            }
            else
            {
                result["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "string"
                };
            }
        }

        if (property.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array)
        {
            var values = enumValues
                .EnumerateArray()
                .Take(20)
                .Select(ReadEnumValue)
                .Where(v => v != null)
                .ToList();

            if (values.Count > 0)
            {
                result["enum"] = values;
            }
        }

        return result;
    }

    private static string ResolveType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var directType) && directType.ValueKind == JsonValueKind.String)
            {
                return directType.GetString() ?? "string";
            }

            if (element.TryGetProperty("properties", out _))
            {
                return "object";
            }

            if (element.TryGetProperty("items", out _))
            {
                return "array";
            }

            foreach (var key in new[] { "oneOf", "anyOf", "allOf" })
            {
                if (!element.TryGetProperty(key, out var candidate) || candidate.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var option in candidate.EnumerateArray())
                {
                    var optionType = ResolveType(option);
                    if (!"null".Equals(optionType, StringComparison.Ordinal))
                    {
                        return optionType;
                    }
                }
            }
        }

        return "string";
    }

    private static object? ReadEnumValue(JsonElement enumEntry)
    {
        return enumEntry.ValueKind switch
        {
            JsonValueKind.String => enumEntry.GetString(),
            JsonValueKind.Number => enumEntry.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? TryReadPropertyAsString(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        var value = property?.GetValue(target);

        return value?.ToString();
    }

    private static JsonElement? TryReadSchema(object target)
    {
        var property = target.GetType().GetProperty("JsonSchema");
        var value = property?.GetValue(target);

        if (value is JsonElement jsonElement)
        {
            return jsonElement;
        }

        return null;
    }
}
