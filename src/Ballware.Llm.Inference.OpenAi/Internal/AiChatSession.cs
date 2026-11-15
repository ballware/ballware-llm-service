using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ChatMessage = Ballware.Llm.Inference.ChatMessage;
using ChatRole = Ballware.Llm.Inference.ChatRole;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Ballware.Llm.Inference.OpenAi.Internal;

internal class AiChatSession : IChatSession
{
    private readonly IChatClient chat;
    private readonly ChatOptions options;
    private readonly IReadOnlyList<AITool> availableTools;
    private readonly ILogger<AiChatSession> logger;
    private readonly List<ChatMessage> history = [];

    public AiChatSession(
        IChatClient chat,
        ChatOptions options,
        IReadOnlyList<AITool> availableTools,
        IReadOnlyList<ChatMessage>? initialHistory,
        ILogger<AiChatSession> logger)
    {
        this.chat = chat;
        this.options = options;
        this.availableTools = availableTools;
        this.logger = logger;

        if (initialHistory != null)
        {
            foreach (var message in initialHistory)
            {
                if (message.Role != ChatRole.System && !string.IsNullOrEmpty(message.Content))
                {
                    history.Add(message);
                }
            }
        }
    }

    public IReadOnlyList<ChatMessage> History => history;

    public async IAsyncEnumerable<string> PromptStreamingAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options.Tools = availableTools.ToList();

        var messages = BuildMessages(message);
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

        var responseBuilder = new StringBuilder();
        var completed = false;
        Exception? error = null;
        var responseEnumerator = chat.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator();

        try
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await responseEnumerator.MoveNextAsync();
                }
                catch (ClientResultException ex) when (IsContextLengthError(ex))
                {
                    error = ex;

                    logger.LogWarning(ex,
                        "LLM request exceeded context window.");

                    break;
                }
                catch (ClientResultException ex)
                {
                    error = ex;

                    logger.LogError(ex,
                        "Ollama API returned HTTP {StatusCode}. Response body: {ResponseBody}",
                        ex.Status, ex.Message);

                    break;
                }
                catch (Exception ex)
                {
                    error = ex;

                    logger.LogError(ex, "Unexpected error during streaming response from Ollama API");

                    break;
                }

                if (!hasNext)
                {
                    completed = true;
                    break;
                }

                var responseDelta = GetResponseDelta(responseEnumerator.Current);

                if (string.IsNullOrEmpty(responseDelta))
                {
                    continue;
                }

                responseBuilder.Append(responseDelta);
                yield return responseDelta;
            }
        }
        finally
        {
            await responseEnumerator.DisposeAsync();
        }

        if (!completed)
        {
            throw error ?? new InvalidOperationException("LLM request failed unexpectedly.");
        }

        var responseText = responseBuilder.ToString();

        logger.LogDebug(
            "LLM response (estimated tokens: {EstimatedResponseTokens}): {Response}",
            EstimateTokenCount(responseText),
            responseText);

        if (!string.IsNullOrEmpty(responseText))
        {
            history.Add(new ChatMessage(ChatRole.Assistant, responseText));
        }
    }

    public async Task PromptAsync(
        string message,
        Action<string> tokenHandler,
        CancellationToken cancellationToken = default)
    {
        await foreach (var token in PromptStreamingAsync(message, cancellationToken))
        {
            tokenHandler(token);
        }
    }

    private List<AiChatMessage> BuildMessages(string message)
    {
        var messages = new List<AiChatMessage>();

        foreach (var historyItem in history)
        {
            if (string.IsNullOrEmpty(historyItem.Content))
            {
                continue;
            }

            messages.Add(new AiChatMessage(MapRole(historyItem.Role), historyItem.Content));
        }

        messages.Add(new AiChatMessage(AiChatRole.User, message));

        return messages;
    }

    private static string GetResponseDelta(ChatResponseUpdate response)
    {
        return response.ToString() ?? string.Empty;
    }

    private static AiChatRole MapRole(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => AiChatRole.System,
            ChatRole.User => AiChatRole.User,
            ChatRole.Assistant => AiChatRole.Assistant,
            _ => AiChatRole.User
        };
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }

    private static bool IsContextLengthError(ClientResultException ex)
    {
        if (ex.Status is 400 or 413)
        {
            var message = ex.Message;
            return message.Contains("maximum context length", StringComparison.InvariantCultureIgnoreCase) ||
                   message.Contains("input_tokens", StringComparison.InvariantCultureIgnoreCase) ||
                   message.Contains("context length", StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
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
