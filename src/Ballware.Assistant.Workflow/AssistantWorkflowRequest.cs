namespace Ballware.Assistant.Workflow;

public sealed class AssistantWorkflowRequest
{
    public required string Message { get; init; }

    public required IReadOnlyList<AssistantConversationEntry> History { get; init; }
}
