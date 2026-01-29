using Ballware.Llm.Ollama;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ballware.Llm.Api.Chat;

[Authorize]
public class ChatHub : Hub
{
    private ChatSessionStore SessionStore { get; }
    private IOllamaModelBridge ModelBridge { get; }
    
    public ChatHub(ChatSessionStore sessionStore, IOllamaModelBridge modelBridge)
    {
        SessionStore = sessionStore;
        ModelBridge = modelBridge;
    }

    public override Task OnConnectedAsync()
    {
        SessionStore.Add(new ChatSession(Context.ConnectionId, ModelBridge.StartChat()));
        
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        SessionStore.Drop(Context.ConnectionId);
        
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Prompt(string message)
    {
        await SessionStore.Get(Context.ConnectionId).Chat.PromptAsync(message, (token) => Clients.Caller.SendAsync("Response", token));
    }
}