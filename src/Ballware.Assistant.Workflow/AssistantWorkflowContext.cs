namespace Ballware.Assistant.Workflow;

public sealed class AssistantWorkflowContext
{
    public required Guid TenantId { get; init; }

    public required Guid UserId { get; init; }

    public required IDictionary<string, object> Claims { get; init; }

    public string? UserAccessToken { get; init; }

    public string? Model { get; init; }

    public int? NumCtx { get; init; }

    public string? SystemPrompt { get; init; }
    
    public IReadOnlyList<AssistantWorkflowStageDefinition>? Stages { get; init; }

    public string? StructuredStageInstructionsTemplate { get; init; }

    public string? KnowledgeStageInstructionsTemplate { get; init; }

    public string? InternetStageInstructionsTemplate { get; init; }

    public string? InternetQueryPreparationInstructionsTemplate { get; init; }

    public string? InternetQueryPreparationPromptTemplate { get; init; }

    public string? ConversationPromptTemplate { get; init; }

    public string? InternetPromptTemplate { get; init; }

    public bool EnableStructuredStage { get; init; } = true;

    public bool EnableKnowledgeStage { get; init; } = true;

    public bool EnableInternetStage { get; init; } = true;
}
