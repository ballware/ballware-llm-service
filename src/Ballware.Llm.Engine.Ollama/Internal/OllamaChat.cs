using System;
using System.Text;
using System.Threading.Tasks;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class OllamaChat(Chat chat, IEnumerable<Tool> tools) : IOllamaChat
{
    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();
        
        await foreach (var response in chat.SendAsync(message, tools: tools))
        {
            sb.Append(response);
        }
        
        tokenHandler(sb.ToString());
    }
}