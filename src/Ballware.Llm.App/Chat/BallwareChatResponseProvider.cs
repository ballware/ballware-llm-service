using System.Runtime.CompilerServices;
using Ballware.Llm.Context;
using Ballware.Shared.Authorization;
using DevExpress.AIIntegration.Chat;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AiChatFinishReason = Microsoft.Extensions.AI.ChatFinishReason;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;
using AiTextContent = Microsoft.Extensions.AI.TextContent;
using BallwareChatContextOptions = Ballware.Llm.Inference.ChatContextOptions;
using BallwareChatMessage = Ballware.Llm.Inference.ChatMessage;
using BallwareChatRole = Ballware.Llm.Inference.ChatRole;
using IChatSession = Ballware.Llm.Inference.IChatSession;
using IInferenceEngine = Ballware.Llm.Inference.IInferenceEngine;

namespace Ballware.Llm.App.Chat;

public sealed class BallwareChatResponseProvider(
    IServiceProvider serviceProvider,
    BallwareChatScope chatScope,
    IInferenceEngine inferenceEngine,
    IModelContextProvider modelContextProvider,
    IPrincipalUtils principalUtils,
    AuthenticationStateProvider authenticationStateProvider,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BallwareChatResponseProvider> logger) : IChatResponseProvider
{
    private IChatSession? chatSession;

    public async IAsyncEnumerable<AiChatResponseUpdate> GetResponseAsync(
        IEnumerable<AiChatMessage> messages,
        bool stream,
        string? conversationId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!chatScope.HasContext)
        {
            yield return CreateAssistantUpdate("Unbekannter oder fehlender Chat-Kontext.", conversationId);
            yield break;
        }

        var prompt = ExtractLastUserPrompt(messages);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            yield break;
        }

        var effectiveConversationId = string.IsNullOrWhiteSpace(conversationId)
            ? chatScope.ConversationId
            : conversationId;

        IChatSession? chat = null;
        string? initializationError = null;
        bool cancelledDuringInitialization = false;

        try
        {
            chat = await EnsureChatSessionAsync(prompt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelledDuringInitialization = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize chat session for context {ContextIdentifier}.", chatScope.ContextIdentifier);
            initializationError = $"Fehler beim Verarbeiten der Anfrage: {ex.Message}";
        }

        if (cancelledDuringInitialization)
        {
            yield break;
        }

        if (initializationError != null)
        {
            yield return CreateAssistantUpdate(initializationError, effectiveConversationId);
            yield break;
        }

        if (chat == null)
        {
            yield return CreateAssistantUpdate("Chat-Session konnte nicht erstellt werden.", effectiveConversationId);
            yield break;
        }

        var enumerator = chat.PromptStreamingAsync(prompt, cancellationToken).GetAsyncEnumerator();

        try
        {
            while (true)
            {
                bool hasNext = false;
                string delta = string.Empty;
                string? error = null;
                bool cancelled = false;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();

                    if (hasNext)
                    {
                        delta = enumerator.Current;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to stream chat response for context {ContextIdentifier}.", chatScope.ContextIdentifier);
                    error = $"Fehler beim Verarbeiten der Anfrage: {ex.Message}";
                }

                if (cancelled)
                {
                    break;
                }

                if (hasNext && !string.IsNullOrEmpty(delta))
                {
                    yield return CreateAssistantUpdate(delta, effectiveConversationId);
                }

                if (!hasNext)
                {
                    if (error != null)
                    {
                        yield return CreateAssistantUpdate(error, effectiveConversationId);
                    }

                    break;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private async Task<IChatSession> EnsureChatSessionAsync(string initialPrompt, CancellationToken cancellationToken)
    {
        if (chatSession != null)
        {
            return chatSession;
        }

        var principal = chatScope.Principal ?? (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var tenantId = chatScope.TenantId != Guid.Empty ? chatScope.TenantId : principalUtils.GetUserTenandId(principal);
        var userId = chatScope.UserId != Guid.Empty ? chatScope.UserId : principalUtils.GetUserId(principal);
        var contextIdentifier = chatScope.ContextIdentifier!;
        var userClaims = principalUtils.GetUserClaims(principal);
        var userAccessToken = await GetUserAccessTokenAsync(httpContextAccessor.HttpContext);

        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);
        var welcomeMessage = await modelContextProvider.ProvideWelcomeMessageAsync(tenantId, contextIdentifier);

        var initialHistory = new List<BallwareChatMessage>();

        if (!string.IsNullOrWhiteSpace(welcomeMessage))
        {
            initialHistory.Add(new BallwareChatMessage(BallwareChatRole.Assistant, welcomeMessage));
        }

        cancellationToken.ThrowIfCancellationRequested();

        chatSession = await inferenceEngine.StartChatAsync(
            serviceProvider,
            new BallwareChatContextOptions
            {
                Model = options.Model,
                NumCtx = options.NumCtx,
                SystemPrompt = systemPrompt,
                InitialHistory = initialHistory.Count > 0 ? initialHistory : null
            },
            tenantId,
            userId,
            userClaims,
            userAccessToken);

        return chatSession;
    }

    private static AiChatResponseUpdate CreateAssistantUpdate(string text, string? conversationId)
    {
        return new AiChatResponseUpdate(AiChatRole.Assistant, text)
        {
            ConversationId = conversationId,
            FinishReason = AiChatFinishReason.Stop
        };
    }

    private static string ExtractLastUserPrompt(IEnumerable<AiChatMessage> messages)
    {
        AiChatMessage? lastUserMessage = null;

        foreach (var message in messages)
        {
            if (message.Role == AiChatRole.User)
            {
                lastUserMessage = message;
            }
        }

        if (lastUserMessage == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(lastUserMessage.Text))
        {
            return lastUserMessage.Text;
        }

        foreach (var content in lastUserMessage.Contents ?? [])
        {
            if (content is AiTextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                return textContent.Text;
            }
        }

        return string.Empty;
    }

    private static async Task<string?> GetUserAccessTokenAsync(HttpContext? httpContext)
    {
        if (httpContext == null)
        {
            return null;
        }

        return await httpContext.GetTokenAsync("access_token");
    }
}
