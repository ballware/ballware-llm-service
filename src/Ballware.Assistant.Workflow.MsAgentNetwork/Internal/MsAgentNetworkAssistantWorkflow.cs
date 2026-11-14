using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ballware.Assistant.Workflow.MsAgentNetwork.Configuration;
using Ballware.Llm.Inference.OpenAi.Configuration;
using Ballware.Llm.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace Ballware.Assistant.Workflow.MsAgentNetwork.Internal;

internal sealed class MsAgentNetworkAssistantWorkflow(
    OpenAiInferenceOptions inferenceOptions,
    IToolRegistry toolRegistry,
    IHttpClientFactory httpClientFactory,
    IServiceProvider serviceProvider,
    MsAgentNetworkWorkflowOptions workflowOptions,
    ILogger<MsAgentNetworkAssistantWorkflow> logger) : IAssistantWorkflow
{
    private readonly IChatClient _baseChatClient = new ChatClient(
        inferenceOptions.Model,
        new ApiKeyCredential(inferenceOptions.ApiKey),
        new OpenAIClientOptions
        {
            Endpoint = BuildOpenAiEndpoint(inferenceOptions.Endpoint)
        }).AsIChatClient();

    public async Task<AssistantWorkflowResponse> ExecuteAsync(
        AssistantWorkflowContext context,
        AssistantWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Assistant workflow started for tenant {TenantId}, user {UserId}. Message: {Message}. History entries: {HistoryEntries}",
            context.TenantId,
            context.UserId,
            request.Message,
            request.History.Count);

        var toolCatalog = await BuildToolCatalogAsync(context);
        logger.LogDebug(
            "Assistant workflow tool catalog resolved. Structured: {StructuredToolCount}, Structured+Knowledge: {StructuredAndKnowledgeToolCount}, Internet: {InternetToolCount}",
            toolCatalog.StructuredTools.Count,
            toolCatalog.StructuredAndKnowledgeTools.Count,
            toolCatalog.InternetTools.Count);

        PipelineResult? structuredResult = null;
        PipelineResult? knowledgeResult = null;

        if (IsStageEnabled(context, AssistantWorkflowStageType.StructuredData, context.EnableStructuredStage))
        {
            structuredResult = await ExecuteStructuredStageAsync(context, request, toolCatalog.StructuredTools);
            if (structuredResult.Status == PipelineStatus.Answered)
            {
                logger.LogDebug("Assistant workflow completed in stage {Stage}.", AssistantPipelineStage.StructuredData);
                return new AssistantWorkflowResponse
                {
                    Message = structuredResult.Answer,
                    Stage = AssistantPipelineStage.StructuredData
                };
            }
        }
        else
        {
            logger.LogDebug("Assistant workflow stage {Stage} is disabled, skipping.", AssistantPipelineStage.StructuredData);
        }

        if (IsStageEnabled(context, AssistantWorkflowStageType.KnowledgeBase, context.EnableKnowledgeStage))
        {
            knowledgeResult = await ExecuteKnowledgeStageAsync(context, request, toolCatalog.StructuredAndKnowledgeTools);
            if (knowledgeResult.Status == PipelineStatus.Answered)
            {
                logger.LogDebug("Assistant workflow completed in stage {Stage}.", AssistantPipelineStage.KnowledgeBase);
                return new AssistantWorkflowResponse
                {
                    Message = knowledgeResult.Answer,
                    Stage = AssistantPipelineStage.KnowledgeBase
                };
            }
        }
        else
        {
            logger.LogDebug("Assistant workflow stage {Stage} is disabled, skipping.", AssistantPipelineStage.KnowledgeBase);
        }

        var needsInternet =
            (knowledgeResult?.Status == PipelineStatus.NeedsInternet) ||
            (structuredResult?.Status == PipelineStatus.NeedsInternet);

        if (IsStageEnabled(context, AssistantWorkflowStageType.InternetResearch, context.EnableInternetStage) && toolCatalog.InternetTools.Count > 0 && needsInternet)
        {
            var internetQuery = await BuildInternetQueryAsync(context, request.Message);
            if (!string.IsNullOrWhiteSpace(internetQuery))
            {
                var internetResult = await ExecuteInternetStageAsync(context, internetQuery, toolCatalog.InternetTools);
                if (internetResult.Status == PipelineStatus.Answered)
                {
                    logger.LogDebug("Assistant workflow completed in stage {Stage}.", AssistantPipelineStage.Internet);
                    return new AssistantWorkflowResponse
                    {
                        Message = internetResult.Answer,
                        Stage = AssistantPipelineStage.Internet
                    };
                }

                if (!string.IsNullOrWhiteSpace(internetResult.Answer))
                {
                    logger.LogDebug(
                        "Assistant workflow finished with internet stage fallback output. Stage status: {Status}",
                        internetResult.Status);
                    return new AssistantWorkflowResponse
                    {
                        Message = internetResult.Answer,
                        Stage = AssistantPipelineStage.Internet
                    };
                }
            }
        }
        else if (!IsStageEnabled(context, AssistantWorkflowStageType.InternetResearch, context.EnableInternetStage))
        {
            logger.LogDebug("Assistant workflow stage {Stage} is disabled, skipping.", AssistantPipelineStage.Internet);
        }

        var fallbackMessage = !string.IsNullOrWhiteSpace(knowledgeResult?.Answer)
            ? knowledgeResult.Answer
            : structuredResult?.Answer;

        if (string.IsNullOrWhiteSpace(fallbackMessage))
        {
            fallbackMessage = ResolveFallbackMessage(context);
        }

        logger.LogDebug(
            "Assistant workflow finished with fallback answer. Structured status: {StructuredStatus}. Knowledge status: {KnowledgeStatus}.",
            structuredResult?.Status,
            knowledgeResult?.Status);

        return new AssistantWorkflowResponse
        {
            Message = fallbackMessage,
            Stage = AssistantPipelineStage.KnowledgeBase
        };
    }

    private static string ResolveFallbackMessage(AssistantWorkflowContext context)
    {
        return string.IsNullOrWhiteSpace(context.FallbackMessage)
            ? "Ich konnte mit den aktuell verfuegbaren internen und externen Quellen keine belastbare Antwort erzeugen."
            : context.FallbackMessage!;
    }

    private async Task<PipelineResult> ExecuteStructuredStageAsync(
        AssistantWorkflowContext context,
        AssistantWorkflowRequest request,
        IReadOnlyList<AITool> tools)
    {
        var instructions = ResolveStructuredStageInstructions(context);
        var prompt = BuildPromptWithHistory(request.Message, request.History, context.ConversationPromptTemplate);
        var response = await ExecutePromptWithToolsAsync(context, "StructuredData", prompt, instructions, tools);
        var result = ParsePipelineResult(response, PipelineStatus.NeedsKnowledgeBase);
        LogStageResult("StructuredData", response, result);
        return result;
    }

    private async Task<PipelineResult> ExecuteKnowledgeStageAsync(
        AssistantWorkflowContext context,
        AssistantWorkflowRequest request,
        IReadOnlyList<AITool> tools)
    {
        var instructions = ResolveKnowledgeStageInstructions(context);
        var prompt = BuildPromptWithHistory(request.Message, request.History, context.ConversationPromptTemplate);
        var response = await ExecutePromptWithToolsAsync(context, "KnowledgeBase", prompt, instructions, tools);
        var result = ParsePipelineResult(response, PipelineStatus.CannotAnswer);
        LogStageResult("KnowledgeBase", response, result);
        return result;
    }

    private async Task<PipelineResult> ExecuteInternetStageAsync(
        AssistantWorkflowContext context,
        string internetQuery,
        IReadOnlyList<AITool> tools)
    {
        var instructions = ResolveInternetStageInstructions(context);
        var prompt = BuildInternetPrompt(internetQuery, context.InternetPromptTemplate);
        var response = await ExecutePromptWithToolsAsync(context, "Internet", prompt, instructions, tools);
        var result = ParsePipelineResult(response, PipelineStatus.CannotAnswer);
        LogStageResult("Internet", response, result);
        return result;
    }

    private async Task<string?> BuildInternetQueryAsync(AssistantWorkflowContext context, string userMessage)
    {
        var instructions = ResolveInternetQueryPreparationInstructions(context);
        var prompt = BuildInternetQueryPreparationPrompt(userMessage, context.InternetQueryPreparationPromptTemplate);
        var response = await ExecutePromptWithToolsAsync(context, "InternetQueryPreparation", prompt, instructions, []);
        var query = ExtractJsonStringProperty(response, "query");
        logger.LogDebug("Assistant workflow stage {Stage} raw output: {Output}", "InternetQueryPreparation", response);

        var sanitized = SanitizeInternetQuery(query);
        logger.LogDebug("Assistant workflow stage {Stage} sanitized internet query: {Query}", "InternetQueryPreparation", sanitized);

        return sanitized;
    }

    private async Task<string> ExecutePromptWithToolsAsync(
        AssistantWorkflowContext context,
        string stageName,
        string prompt,
        string instructions,
        IReadOnlyList<AITool> tools)
    {
        var chat = new ChatClientBuilder(_baseChatClient)
            .UseFunctionInvocation()
            .Build();

        var options = new ChatOptions
        {
            ModelId = context.Model ?? inferenceOptions.Model,
            Instructions = instructions
        };

        if (context.NumCtx != null)
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["num_ctx"] = context.NumCtx
            };
        }

        options.Tools = tools.ToList();
        var toolsSnapshot = BuildToolsSnapshot(tools);

        logger.LogDebug(
            "Assistant workflow stage {Stage} request (estimated tokens total: {EstimatedTotalTokens}, instructions: {EstimatedInstructionsTokens}, prompt: {EstimatedPromptTokens}, tools: {EstimatedToolsTokens}, tools count: {ToolCount}). Instructions: {Instructions}. Prompt: {Prompt}. Tools: {ToolsSnapshot}",
            stageName,
            EstimateTokenCount(instructions) + EstimateTokenCount(prompt) + EstimateTokenCount(toolsSnapshot),
            EstimateTokenCount(instructions),
            EstimateTokenCount(prompt),
            EstimateTokenCount(toolsSnapshot),
            tools.Count,
            instructions,
            prompt,
            toolsSnapshot);

        var sb = new StringBuilder();

        try
        {
            await foreach (var response in chat.GetStreamingResponseAsync(prompt, options))
            {
                sb.Append(response);
            }
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex, "Assistant workflow stage request failed with HTTP {StatusCode}", ex.Status);
            throw;
        }

        var stageOutput = sb.ToString();
        logger.LogDebug(
            "Assistant workflow stage {Stage} output (estimated tokens: {EstimatedOutputTokens}): {Output}",
            stageName,
            EstimateTokenCount(stageOutput),
            stageOutput);

        return stageOutput;
    }

    private async Task<ToolCatalog> BuildToolCatalogAsync(AssistantWorkflowContext context)
    {
        var structuredTools = new List<AITool>();
        var knowledgeTools = new List<AITool>();
        var internetTools = new List<AITool>();

        foreach (var provider in toolRegistry.StaticToolProviders)
        {
            AddLocalTools(provider.GetToolsForUser(context.TenantId, context.UserId, context.Claims), structuredTools, knowledgeTools);
        }

        foreach (var providerType in toolRegistry.ScopedToolProviderTypes)
        {
            var provider = (IToolProvider)serviceProvider.GetRequiredService(providerType);
            AddLocalTools(provider.GetToolsForUser(context.TenantId, context.UserId, context.Claims), structuredTools, knowledgeTools);
        }

        foreach (var mcpService in toolRegistry.McpServices)
        {
            var mcpTools = await ResolveMcpToolsAsync(mcpService, context);
            if (mcpTools.Count == 0)
            {
                continue;
            }

            if (IsInternetMcpService(mcpService.Identifier))
            {
                internetTools.AddRange(mcpTools);
            }
            else
            {
                foreach (var mcpTool in mcpTools)
                {
                    if (LooksLikeInternetTool(mcpTool))
                    {
                        internetTools.Add(mcpTool);
                    }
                    else
                    {
                        structuredTools.Add(mcpTool);
                    }
                }
            }
        }

        var structuredAndKnowledgeTools = structuredTools.Concat(knowledgeTools).ToList();

        return new ToolCatalog(structuredTools, structuredAndKnowledgeTools, internetTools);
    }

    private void LogStageResult(string stageName, string rawOutput, PipelineResult result)
    {
        logger.LogDebug(
            "Assistant workflow stage {Stage} parsed result. Status: {Status}. Answer: {Answer}. RawOutput: {RawOutput}",
            stageName,
            result.Status,
            result.Answer,
            rawOutput);
    }

    private void AddLocalTools(IEnumerable<Tool> tools, ICollection<AITool> structuredTools, ICollection<AITool> knowledgeTools)
    {
        foreach (var tool in tools)
        {
            var aiTool = new MsAgentCallbackTool(tool);

            if (workflowOptions.KnowledgeToolNamePrefixes.Any(prefix =>
                    tool.Name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase)))
            {
                knowledgeTools.Add(aiTool);
            }
            else
            {
                structuredTools.Add(aiTool);
            }
        }
    }

    private async Task<List<AITool>> ResolveMcpToolsAsync(
        IMcpService mcpService,
        AssistantWorkflowContext context)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(mcpService.Identifier);

            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = httpClient.BaseAddress ?? new Uri("/")
            };

            if (mcpService.UserTokenDelegation && !string.IsNullOrWhiteSpace(context.UserAccessToken))
            {
                transportOptions.AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {context.UserAccessToken}"
                };
            }

            var transport = new HttpClientTransport(transportOptions, httpClient);
            var mcpClient = await McpClient.CreateAsync(transport);
            var mcpTools = await mcpClient.ListToolsAsync();

            return mcpTools.ToList<AITool>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve MCP tools for service '{Identifier}'", mcpService.Identifier);
            return [];
        }
    }

    private bool IsInternetMcpService(string identifier)
    {
        if (workflowOptions.InternetMcpServiceIdentifiers.Any(i =>
                i.Equals(identifier, StringComparison.InvariantCultureIgnoreCase)))
        {
            return true;
        }

        return identifier.Contains(".internet", StringComparison.InvariantCultureIgnoreCase) ||
               identifier.Contains(".web", StringComparison.InvariantCultureIgnoreCase) ||
               identifier.Contains(".search", StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool LooksLikeInternetTool(AITool tool)
    {
        var name = tool.GetType().GetProperty("Name")?.GetValue(tool)?.ToString() ?? string.Empty;

        return name.Contains("internet", StringComparison.InvariantCultureIgnoreCase) ||
               name.Contains("web", StringComparison.InvariantCultureIgnoreCase) ||
               name.Contains("search", StringComparison.InvariantCultureIgnoreCase) ||
               name.Contains("news", StringComparison.InvariantCultureIgnoreCase) ||
               name.Contains("wikipedia", StringComparison.InvariantCultureIgnoreCase);
    }

    private static string BuildToolsSnapshot(IReadOnlyList<AITool> tools)
    {
        if (tools.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();

        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var name = tool.GetType().GetProperty("Name")?.GetValue(tool)?.ToString();
            var description = tool.GetType().GetProperty("Description")?.GetValue(tool)?.ToString();

            builder.AppendLine($"[{i}] Name: {name ?? "(n/a)"}");
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.AppendLine($"Description: {description}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildPromptWithHistory(string message, IReadOnlyList<AssistantConversationEntry> history, string? promptTemplate)
    {
        var historyText = BuildHistoryText(history);
        
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            if (string.IsNullOrWhiteSpace(historyText))
            {
                return message;
            }

            return historyText + Environment.NewLine + Environment.NewLine + message;
        }
        
        return ApplyTemplate(promptTemplate!,
            ("History", historyText),
            ("Message", message));
    }

    private static string BuildInternetPrompt(string internetQuery, string? promptTemplate)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            return internetQuery;
        }
        
        return ApplyTemplate(promptTemplate!, ("InternetQuery", internetQuery));
    }

    private static string BuildInternetQueryPreparationPrompt(string message, string? promptTemplate)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            return message;
        }
        
        return ApplyTemplate(promptTemplate!, ("Message", message));
    }

    private static string BuildHistoryText(IReadOnlyList<AssistantConversationEntry> history)
    {
        var sb = new StringBuilder();

        foreach (var entry in history)
        {
            sb.Append(entry.Role.Equals("assistant", StringComparison.InvariantCultureIgnoreCase)
                ? "Assistant: "
                : "User: ");
            sb.AppendLine(entry.Content);
        }

        return sb.ToString().TrimEnd();
    }

    private static string ResolveStructuredStageInstructions(AssistantWorkflowContext context)
    {
        return BuildStageInstructions(
            context.SystemPrompt,
            ResolveStage(context, AssistantWorkflowStageType.StructuredData)?.InstructionsTemplate ?? context.StructuredStageInstructionsTemplate,
            prependSystemPrompt: true);
    }

    private static string ResolveKnowledgeStageInstructions(AssistantWorkflowContext context)
    {
        return BuildStageInstructions(
            context.SystemPrompt,
            ResolveStage(context, AssistantWorkflowStageType.KnowledgeBase)?.InstructionsTemplate ?? context.KnowledgeStageInstructionsTemplate,
            prependSystemPrompt: true);
    }

    private static string ResolveInternetStageInstructions(AssistantWorkflowContext context)
    {
        return BuildStageInstructions(
            context.SystemPrompt,
            ResolveStage(context, AssistantWorkflowStageType.InternetResearch)?.InstructionsTemplate ?? context.InternetStageInstructionsTemplate,
            prependSystemPrompt: false);
    }

    private static string ResolveInternetQueryPreparationInstructions(AssistantWorkflowContext context)
    {
        return BuildStageInstructions(
            context.SystemPrompt,
            context.InternetQueryPreparationInstructionsTemplate,
            prependSystemPrompt: false);
    }

    private static AssistantWorkflowStageDefinition? ResolveStage(AssistantWorkflowContext context, AssistantWorkflowStageType type)
    {
        return context.Stages?.FirstOrDefault(s => s.Type == type);
    }

    private static bool IsStageEnabled(AssistantWorkflowContext context, AssistantWorkflowStageType type, bool fallbackEnabled)
    {
        return ResolveStage(context, type)?.Enabled ?? fallbackEnabled;
    }

    private static string BuildStageInstructions(
        string? systemPrompt,
        string? stageInstructionsTemplate,
        bool prependSystemPrompt)
    {
        var template = string.IsNullOrWhiteSpace(stageInstructionsTemplate)
            ? string.Empty
            : stageInstructionsTemplate!;

        var hasSystemPromptPlaceholder = template.Contains("{SystemPrompt}", StringComparison.InvariantCultureIgnoreCase);
        var resolved = ApplyTemplate(template, ("SystemPrompt", systemPrompt ?? string.Empty));

        if (!prependSystemPrompt || string.IsNullOrWhiteSpace(systemPrompt) || hasSystemPromptPlaceholder)
        {
            return resolved;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return systemPrompt;
        }

        return systemPrompt + Environment.NewLine + Environment.NewLine + resolved;
    }

    private static string ApplyTemplate(string template, params (string Placeholder, string Value)[] replacements)
    {
        var result = template;

        foreach (var replacement in replacements)
        {
            result = result.Replace("{" + replacement.Placeholder + "}", replacement.Value ?? string.Empty, StringComparison.InvariantCultureIgnoreCase);
        }

        return result;
    }

    private static PipelineResult ParsePipelineResult(string response, PipelineStatus fallbackStatus)
    {
        if (!TryParseJson(response, out var root))
        {
            return new PipelineResult(PipelineStatus.Answered, response);
        }

        var statusText = ReadString(root, "status");
        var answer = ReadString(root, "answer") ?? response;

        var status = statusText?.Trim().ToUpperInvariant() switch
        {
            "ANSWERED" => PipelineStatus.Answered,
            "NEED_KNOWLEDGE_BASE" => PipelineStatus.NeedsKnowledgeBase,
            "NEED_INTERNET" => PipelineStatus.NeedsInternet,
            "CANNOT_ANSWER" => PipelineStatus.CannotAnswer,
            _ => fallbackStatus
        };

        return new PipelineResult(status, answer);
    }

    private static string? ExtractJsonStringProperty(string response, string propertyName)
    {
        if (!TryParseJson(response, out var root))
        {
            return null;
        }

        return ReadString(root, propertyName);
    }

    private static bool TryParseJson(string response, out JsonElement root)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            root = default;
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    private static string? ExtractJson(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            return trimmed;
        }

        var fencedStart = trimmed.IndexOf("```json", StringComparison.InvariantCultureIgnoreCase);
        if (fencedStart >= 0)
        {
            var start = trimmed.IndexOf('{', fencedStart);
            var endFence = trimmed.IndexOf("```", start, StringComparison.InvariantCultureIgnoreCase);
            if (start >= 0 && endFence > start)
            {
                return trimmed[start..endFence].Trim();
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return null;
    }

    private static string? SanitizeInternetQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var sanitized = query.Trim();
        sanitized = Regex.Replace(sanitized,
            @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b",
            "[redacted-guid]");
        sanitized = Regex.Replace(sanitized, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[redacted-email]",
            RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\b\d{7,}\b", "[redacted-number]");
        sanitized = sanitized.Replace('\n', ' ').Replace('\r', ' ');
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

        if (sanitized.Length > 300)
        {
            sanitized = sanitized[..300];
        }

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static Uri BuildOpenAiEndpoint(string baseUrl)
    {
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var currentPath = baseUri.AbsolutePath.TrimEnd('/');

        if (string.IsNullOrEmpty(currentPath) || currentPath == "/")
        {
            return new Uri(baseUri, "v1");
        }

        if (currentPath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return baseUri;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{currentPath}/v1"
        };

        return builder.Uri;
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }

    private sealed record ToolCatalog(
        IReadOnlyList<AITool> StructuredTools,
        IReadOnlyList<AITool> StructuredAndKnowledgeTools,
        IReadOnlyList<AITool> InternetTools);

    private sealed record PipelineResult(PipelineStatus Status, string Answer);

    private enum PipelineStatus
    {
        Answered,
        NeedsKnowledgeBase,
        NeedsInternet,
        CannotAnswer
    }
}
