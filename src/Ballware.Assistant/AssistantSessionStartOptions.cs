using Ballware.Assistant.Workflow;

namespace Ballware.Assistant;

public class AssistantSessionStartOptions
{
    public required Guid TenantId { get; init; }

    public required Guid UserId { get; init; }

    public required IDictionary<string, object> Claims { get; init; }

    public string? UserAccessToken { get; init; }

    public string? Model { get; init; }

    public int? NumCtx { get; init; }

    public string? SystemPrompt { get; init; }

    public string? FallbackMessage { get; init; }
    
    public IReadOnlyList<AssistantWorkflowStageDefinition>? Stages { get; init; }
}
