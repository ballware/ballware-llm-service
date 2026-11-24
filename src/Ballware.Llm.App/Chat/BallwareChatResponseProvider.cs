using System.Runtime.CompilerServices;
using System.Text;
using Ballware.Llm.Context;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Authorization;
using DevExpress.AIIntegration.Chat;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    IChatSessionRepository chatSessionRepository,
    IOptions<ChatSessionOptions> chatSessionOptions,
    ILogger<BallwareChatResponseProvider> logger) : IChatResponseProvider
{
    private const string DefaultTitle = "Unterhaltung";

    private static readonly int MaxAutoTitleLength = 100;

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

        var turnSessionId = await EnsurePersistedSessionAsync(prompt);

        var turn = new TurnState();

        await foreach (var update in StreamTurnAsync(chat, prompt, effectiveConversationId, turn, cancellationToken))
        {
            yield return update;
        }

        await PersistTurnAsync(turnSessionId, prompt, turn);

        if (turnSessionId.HasValue)
        {
            chatScope.NotifySessionUpdated();
        }
    }

    private async IAsyncEnumerable<AiChatResponseUpdate> StreamTurnAsync(
        IChatSession chat,
        string prompt,
        string? conversationId,
        TurnState turn,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
                    turn.Cancelled = true;

                    break;
                }

                if (hasNext && !string.IsNullOrEmpty(delta))
                {
                    turn.AssistantText.Append(delta);

                    yield return CreateAssistantUpdate(delta, conversationId);
                }

                if (!hasNext)
                {
                    if (error != null)
                    {
                        turn.Failed = true;

                        yield return CreateAssistantUpdate(error, conversationId);
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

        var initialHistory = new List<BallwareChatMessage>();
        var resumedSessionId = chatScope.SessionId;

        if (resumedSessionId.HasValue)
        {
            var history = await chatSessionRepository.GetMessagesAsync(
                tenantId,
                userId,
                resumedSessionId.Value,
                chatSessionOptions.Value.MaxHistoryMessages);

            foreach (var message in history)
            {
                initialHistory.Add(new BallwareChatMessage(MapRole(message.Role), message.Content));
            }
        }
        else
        {
            var welcomeMessage = await modelContextProvider.ProvideWelcomeMessageAsync(tenantId, contextIdentifier);

            if (!string.IsNullOrWhiteSpace(welcomeMessage))
            {
                initialHistory.Add(new BallwareChatMessage(BallwareChatRole.Assistant, welcomeMessage));
            }
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

    private async Task<Guid?> EnsurePersistedSessionAsync(string prompt)
    {
        if (chatScope.HasSession)
        {
            return chatScope.SessionId;
        }

        try
        {
            var session = await chatSessionRepository.CreateAsync(
                chatScope.TenantId,
                chatScope.UserId,
                chatScope.ContextIdentifier!,
                AutoTitle(prompt));

            chatScope.SetSession(session.Id);

            return session.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create chat session for context {ContextIdentifier}; continuing without persistence.",
                chatScope.ContextIdentifier);

            return null;
        }
    }

    private async Task PersistTurnAsync(Guid? sessionId, string prompt, TurnState turn)
    {
        if (!sessionId.HasValue)
        {
            return;
        }

        var messages = new List<ChatSessionMessage>
        {
            new()
            {
                Role = Ballware.Llm.Data.Persistables.ChatMessage.RoleUser,
                Content = prompt
            }
        };

        var assistantText = turn.AssistantText.ToString();

        if (!turn.Failed && !string.IsNullOrEmpty(assistantText))
        {
            messages.Add(new ChatSessionMessage
            {
                Role = Ballware.Llm.Data.Persistables.ChatMessage.RoleAssistant,
                Content = assistantText
            });
        }

        try
        {
            await chatSessionRepository.AppendMessagesAsync(chatScope.TenantId, chatScope.UserId, sessionId.Value, messages);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to persist chat turn for session {SessionId}; chat continues without persistence for this turn.",
                sessionId);
        }
    }

    private static BallwareChatRole MapRole(string? role)
    {
        return role switch
        {
            Ballware.Llm.Data.Persistables.ChatMessage.RoleAssistant => BallwareChatRole.Assistant,
            Ballware.Llm.Data.Persistables.ChatMessage.RoleSystem => BallwareChatRole.System,
            _ => BallwareChatRole.User
        };
    }

    private static string AutoTitle(string prompt)
    {
        var title = new string(prompt.Trim().Select(character => character == '\r' || character == '\n' || character == '\t' ? ' ' : character).ToArray());

        while (title.Contains("  "))
        {
            title = title.Replace("  ", " ");
        }

        title = title.Trim();

        if (string.IsNullOrEmpty(title))
        {
            return DefaultTitle;
        }

        return title.Length > MaxAutoTitleLength ? title[..MaxAutoTitleLength].Trim() : title;
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

    private sealed class TurnState
    {
        public StringBuilder AssistantText { get; } = new();
        public bool Cancelled { get; set; }
        public bool Failed { get; set; }
    }
}
