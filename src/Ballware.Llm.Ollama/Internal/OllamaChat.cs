using System;
using System.Text;
using System.Threading.Tasks;
using OllamaSharp;
using OllamaSharp.Models;

namespace Ballware.Llm.Ollama.Internal;

internal class OllamaChat(Chat chat) : IOllamaChat
{
    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();
        
        await foreach (var response in chat.SendAsync(message))
        {
            sb.Append(response);
        }
        
        tokenHandler(sb.ToString());
    }
}