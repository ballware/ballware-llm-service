using System.Security.Claims;
using System.Text.Json;
using Ballware.Llm.Api.Chat;
using Ballware.Llm.Api.Model;
using Ballware.Llm.Context;
using Ballware.Llm.Inference;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Api.Endpoints;

public static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app, string basePath)
    {
        app.MapHub<ChatHub>(basePath + "/chat");
        
        return app;
    }

    public static IEndpointRouteBuilder MapOpenAiChatCompletionsEndpoint(this IEndpointRouteBuilder app, string basePath)
    {
        app.MapPost(basePath + "/chat/completions", HandleOpenAiChatCompletion)
            .RequireAuthorization()
            .DisableAntiforgery()
            .Produces<OpenAiChatCompletionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("OpenAiChatCompletions")
            .WithGroupName("llm")
            .WithTags("Chat")
            .WithSummary("OpenAI compatible chat completions endpoint");

        return app;
    }

    private static async Task<IResult> HandleOpenAiChatCompletion(
        HttpContext httpContext,
        IServiceProvider serviceProvider,
        IPrincipalUtils principalUtils,
        IModelContextProvider modelContextProvider,
        IInferenceEngine inferenceEngine,
        ClaimsPrincipal user,
        OpenAiChatCompletionRequest body,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(body.Model))
        {
            return Results.BadRequest("The requested model must contain the context identifier.");
        }

        if (body.Messages.Count == 0)
        {
            return Results.BadRequest("At least one message is required.");
        }

        var prompt = BuildPrompt(body.Messages);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Results.BadRequest("At least one message with content is required.");
        }

        var tenantId = principalUtils.GetUserTenandId(user);
        var userId = principalUtils.GetUserId(user);
        var userClaims = principalUtils.GetUserClaims(user);
        var userAccessToken = await httpContext.GetTokenAsync("access_token");
        var contextIdentifier = body.Model;
        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, prompt);
        var logger = loggerFactory.CreateLogger("Ballware.Llm.Api.OpenAiChatCompletions");

        logger.LogDebug(
            "LLM system prompt (OpenAI chat completions, context: {ContextIdentifier}, estimated tokens: {EstimatedSystemPromptTokens}): {SystemPrompt}",
            contextIdentifier,
            EstimateTokenCount(systemPrompt),
            systemPrompt);

        var chat = await inferenceEngine.StartChatAsync(serviceProvider, new ChatContextOptions()
        {
            Model = options.Model,
            NumCtx = options.NumCtx,
            SystemPrompt = systemPrompt
        }, tenantId, userId, userClaims, userAccessToken);

        var responseText = string.Empty;
        await chat.PromptAsync(prompt, token => responseText += token);

        return body.Stream
            ? Results.Stream(async stream => await WriteStreamingResponseAsync(stream, body.Model, responseText), "text/event-stream")
            : Results.Ok(BuildResponse(body.Model, responseText));
    }

    private static string BuildPrompt(IEnumerable<OpenAiChatCompletionMessage> messages)
    {
        return string.Join(Environment.NewLine, messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? message.Content
                : $"{message.Role}: {message.Content}"));
    }

    private static OpenAiChatCompletionResponse BuildResponse(string model, string responseText)
    {
        return new OpenAiChatCompletionResponse()
        {
            Id = $"chatcmpl-{Guid.NewGuid():N}",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model,
            Choices = new List<OpenAiChatCompletionChoice>()
            {
                new()
                {
                    Index = 0,
                    Message = new OpenAiChatCompletionMessage()
                    {
                        Role = "assistant",
                        Content = responseText
                    },
                    FinishReason = "stop"
                }
            }
        };
    }

    private static async Task WriteStreamingResponseAsync(Stream stream, string model, string responseText)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = $"chatcmpl-{Guid.NewGuid():N}";

        await WriteServerSentEventAsync(stream, new OpenAiChatCompletionStreamResponse()
        {
            Id = id,
            Created = created,
            Model = model,
            Choices = new List<OpenAiChatCompletionStreamChoice>()
            {
                new()
                {
                    Index = 0,
                    Delta = new OpenAiChatCompletionMessage()
                    {
                        Role = "assistant",
                        Content = responseText
                    }
                }
            }
        });

        await WriteServerSentEventAsync(stream, new OpenAiChatCompletionStreamResponse()
        {
            Id = id,
            Created = created,
            Model = model,
            Choices = new List<OpenAiChatCompletionStreamChoice>()
            {
                new()
                {
                    Index = 0,
                    Delta = new OpenAiChatCompletionMessage(),
                    FinishReason = "stop"
                }
            }
        });

        await using var writer = new StreamWriter(stream, leaveOpen: true);
        await writer.WriteAsync("data: [DONE]\n\n");
        await writer.FlushAsync();
    }

    private static async Task WriteServerSentEventAsync<T>(Stream stream, T payload)
    {
        await using var writer = new StreamWriter(stream, leaveOpen: true);
        await writer.WriteAsync("data: ");
        await writer.WriteAsync(JsonSerializer.Serialize(payload));
        await writer.WriteAsync("\n\n");
        await writer.FlushAsync();
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }
}
