using System.Text;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class AiChat(IChatClient chat, ChatOptions options) : IAiChat
{
    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();
        
        await foreach (var response in chat.GetStreamingResponseAsync(message, options))
        {
            sb.Append(response);
        }
        
        tokenHandler(sb.ToString());
    }
}