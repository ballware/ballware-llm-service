namespace Ballware.Assistant.Workflow;

public sealed class AssistantWorkflowResponse
{
    public required string Message { get; init; }

    public required AssistantPipelineStage Stage { get; init; }
}
