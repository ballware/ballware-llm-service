using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class AiChat(IChatClient chat, ChatOptions options, ILogger<AiChat> logger) : IAiChat
{
    // Einige Modelle liefern bei Streaming internisierte Thinking-/BPE-Artefakte.
    private static readonly Regex ThinkBlockRegex = new("<think>[\\s\\S]*?</think>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThinkCloseRegex = new("</think>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();

        try
        {
            await foreach (var response in chat.GetStreamingResponseAsync(message, options))
            {
                sb.Append(response);
            }

            tokenHandler(sb.ToString());
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
}