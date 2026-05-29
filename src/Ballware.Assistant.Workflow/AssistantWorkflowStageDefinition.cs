namespace Ballware.Assistant.Workflow;

public sealed class AssistantWorkflowStageDefinition
{
    public AssistantWorkflowStageType Type { get; init; }
    public bool Enabled { get; init; } = true;
    public string? InstructionsTemplate { get; init; }
    public string? PromptTemplate { get; init; }
}
