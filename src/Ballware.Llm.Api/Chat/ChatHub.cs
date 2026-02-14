using Ballware.Llm.Context;
using Ballware.Llm.Metadata;
using Ballware.Llm.Engine.Ollama;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Api.Chat;

[Authorize]
public class ChatHub(IServiceProvider serviceProvider, IPrincipalUtils principalUtils, ChatSessionStore sessionStore) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.Identity == null || !Context.User.Identity.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }
        
        var tenantId = principalUtils.GetUserTenandId(Context.User);
        var userId = principalUtils.GetUserId(Context.User);
        var userClaims = principalUtils.GetUserClaims(Context.User);
        
        sessionStore.Add(new ChatSession(Context.ConnectionId, "global", tenantId, userId, userClaims));
        
        await base.OnConnectedAsync();
        
        var scope = serviceProvider.CreateScope();
        var modelContextProvider = scope.ServiceProvider.GetRequiredService<IModelContextProvider>();
        
        var welcomeMessage = await modelContextProvider.ProvideWelcomeMessageAsync(tenantId, "global");
        
        if (!string.IsNullOrEmpty(welcomeMessage))
        {
            await Clients.Caller.SendAsync("Response", welcomeMessage);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        sessionStore.Drop(Context.ConnectionId);
        
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Prompt(string message)
    {
        var session = sessionStore.Get(Context.ConnectionId);

        if (session.Chat == null)
        {
            var scope = serviceProvider.CreateScope();
            var ollamaModelBridge = scope.ServiceProvider.GetRequiredService<IOllamaModelBridge>();
            var modelContextProvider = scope.ServiceProvider.GetRequiredService<IModelContextProvider>();
            
            await session.StartChatAsync(ollamaModelBridge, modelContextProvider, message);
        }
        
        await session.Chat.PromptAsync(message, (token) => Clients.Caller.SendAsync("Response", token));
    }
}