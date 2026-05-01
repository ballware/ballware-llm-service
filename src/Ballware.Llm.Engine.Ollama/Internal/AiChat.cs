using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class AiChat(IChatClient chat, ChatOptions options, ILogger<AiChat> logger) : IAiChat
{
    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();

        try
        {
            var requestSnapshot = BuildRequestSnapshot(message, options);

            logger.LogDebug(
                "LLM request payload (estimated tokens total: {EstimatedTotalTokens}, instructions: {EstimatedInstructionsTokens}, prompt: {EstimatedPromptTokens}, tools: {EstimatedToolsTokens}, tools count: {ToolCount}): {RequestPayload}",
                requestSnapshot.EstimatedTotalTokens,
                requestSnapshot.EstimatedInstructionsTokens,
                requestSnapshot.EstimatedPromptTokens,
                requestSnapshot.EstimatedToolsTokens,
                requestSnapshot.ToolCount,
                requestSnapshot.Payload);

            logger.LogDebug(
                "LLM prompt (estimated tokens: {EstimatedPromptTokens}): {Prompt}",
                EstimateTokenCount(message),
                message);

            await foreach (var response in chat.GetStreamingResponseAsync(message, options))
            {
                sb.Append(response);
            }

            var responseText = sb.ToString();

            logger.LogDebug(
                "LLM response (estimated tokens: {EstimatedResponseTokens}): {Response}",
                EstimateTokenCount(responseText),
                responseText);

            tokenHandler(responseText);
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex,
                "Ollama API returned HTTP {StatusCode}. Response body: {ResponseBody}",
                ex.Status, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during streaming response from Ollama API");
            throw;
        }
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }

    private static RequestSnapshot BuildRequestSnapshot(string message, ChatOptions options)
    {
        var instructions = options.Instructions ?? string.Empty;
        var toolsText = BuildToolsSnapshot(options.Tools);

        var payloadBuilder = new StringBuilder();
        payloadBuilder.AppendLine("INSTRUCTIONS:");
        payloadBuilder.AppendLine(instructions);
        payloadBuilder.AppendLine();
        payloadBuilder.AppendLine("USER_PROMPT:");
        payloadBuilder.AppendLine(message);
        payloadBuilder.AppendLine();
        payloadBuilder.AppendLine("TOOLS:");
        payloadBuilder.AppendLine(toolsText);

        var payload = payloadBuilder.ToString();

        return new RequestSnapshot(
            payload,
            EstimateTokenCount(instructions),
            EstimateTokenCount(message),
            EstimateTokenCount(toolsText),
            options.Tools?.Count ?? 0);
    }

    private static string BuildToolsSnapshot(IList<AITool>? tools)
    {
        if (tools == null || tools.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();

        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];

            builder.AppendLine($"[{i}]");
            builder.AppendLine($"Type: {tool.GetType().FullName}");

            var name = TryReadPropertyAsString(tool, "Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.AppendLine($"Name: {name}");
            }

            var description = TryReadPropertyAsString(tool, "Description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.AppendLine($"Description: {description}");
            }

            var schema = TryReadSchema(tool);
            if (!string.IsNullOrWhiteSpace(schema))
            {
                builder.AppendLine("JsonSchema:");
                builder.AppendLine(schema);
            }

            if (i < tools.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? TryReadPropertyAsString(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        var value = property?.GetValue(target);

        return value?.ToString();
    }

    private static string? TryReadSchema(object target)
    {
        var property = target.GetType().GetProperty("JsonSchema");
        var schema = property?.GetValue(target);

        if (schema == null)
        {
            return null;
        }

        if (schema is JsonElement jsonElement)
        {
            return jsonElement.GetRawText();
        }

        try
        {
            return JsonSerializer.Serialize(schema);
        }
        catch
        {
            return schema.ToString();
        }
    }

    private sealed record RequestSnapshot(
        string Payload,
        long EstimatedInstructionsTokens,
        long EstimatedPromptTokens,
        long EstimatedToolsTokens,
        int ToolCount)
    {
        public long EstimatedTotalTokens => EstimatedInstructionsTokens + EstimatedPromptTokens + EstimatedToolsTokens;
    }
}
