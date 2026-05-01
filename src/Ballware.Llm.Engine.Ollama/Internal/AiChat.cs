using System.ClientModel;
using System.Text;
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
}
