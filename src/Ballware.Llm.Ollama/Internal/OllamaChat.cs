using System.Text;
using OllamaSharp;

namespace Ballware.Llm.Ollama.Internal;

internal class OllamaChat : IOllamaChat
{
    private Chat Chat { get; }
    
    public OllamaChat(Chat chat)
    {
        Chat = chat;
    }
    
    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var sb = new StringBuilder();
        
        await foreach (var response in Chat.SendAsync(message))
        {
            sb.Append(response);
        }
        
        tokenHandler(sb.ToString());
    }
}