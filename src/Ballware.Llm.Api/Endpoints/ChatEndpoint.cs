using Ballware.Llm.Api.Chat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ballware.Llm.Api.Endpoints;

public static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app, string basePath)
    {
        app.MapHub<ChatHub>(basePath + "/chat");
        
        return app;
    }
}