using Ballware.Assistant;
using Ballware.Llm.Context;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Api.Chat;

[Authorize]
public class ChatHub(IServiceProvider serviceProvider, IPrincipalUtils principalUtils, ChatSessionStore sessionStore, ILogger<ChatHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        
        if (Context.User?.Identity == null || !Context.User.Identity.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }

        if (httpContext == null)
        {
            throw new ArgumentNullException(nameof(httpContext));
        }
        
        var tenantId = principalUtils.GetUserTenandId(Context.User);
        var userId = principalUtils.GetUserId(Context.User);
        var userClaims = principalUtils.GetUserClaims(Context.User);
        var userAccessToken = await httpContext.GetTokenAsync("access_token");
        
        sessionStore.Add(new ChatSession(Context.ConnectionId, "global", tenantId, userId, userClaims, userAccessToken));
        
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

        try
        {
            if (session.Chat == null)
            {
                var scope = serviceProvider.CreateScope();
                var assistantSessionFactory = scope.ServiceProvider.GetRequiredService<IAssistantSessionFactory>();
                var modelContextProvider = scope.ServiceProvider.GetRequiredService<IModelContextProvider>();

                await session.StartChatAsync(scope.ServiceProvider, assistantSessionFactory, modelContextProvider, message);
            }

            await session.Chat!.PromptAsync(message, (token) => Clients.Caller.SendAsync("Response", token));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing prompt for connection {ConnectionId}. Message: {Message}", Context.ConnectionId, message);
            await Clients.Caller.SendAsync("Error", $"Fehler beim Verarbeiten der Anfrage: {ex.Message}");
            throw;
        }
    }
}
