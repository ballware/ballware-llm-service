using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ballware.Llm.Api.Chat;

[Authorize]
public class ChatHub : Hub
{
    public async Task Prompt(string message)
    {
        await Clients.All.SendAsync("Response", message + " received");
    }
}