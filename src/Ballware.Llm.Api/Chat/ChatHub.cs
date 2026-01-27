using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ballware.Llm.Api.Chat;

[Authorize]
public class ChatHub : Hub
{
    public Task Ping() => Clients.All.SendAsync("Pong");
}