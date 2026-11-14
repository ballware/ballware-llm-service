namespace Ballware.Llm.Context;

using Ballware.Assistant.Workflow;

public class ModelContextOptions
{
    public string? Model { get; set; }
    public int? NumCtx { get; set; }
    public string? AssistantFallbackMessage { get; set; }
    
    public IReadOnlyList<AssistantWorkflowStageDefinition>? AssistantStages { get; set; }

    public string? AssistantStructuredStageInstructionsTemplate { get; set; }
    public string? AssistantKnowledgeStageInstructionsTemplate { get; set; }
    public string? AssistantInternetStageInstructionsTemplate { get; set; }
    public string? AssistantInternetQueryPreparationInstructionsTemplate { get; set; }
    public string? AssistantInternetQueryPreparationPromptTemplate { get; set; }
    public string? AssistantConversationPromptTemplate { get; set; }
    public string? AssistantInternetPromptTemplate { get; set; }

    public bool AssistantEnableStructuredStage { get; set; } = true;
    public bool AssistantEnableKnowledgeStage { get; set; } = true;
    public bool AssistantEnableInternetStage { get; set; } = true;
}
