namespace Ballware.Assistant.Workflow;

public interface IAssistantWorkflow
{
    Task<AssistantWorkflowResponse> ExecuteAsync(AssistantWorkflowContext context, AssistantWorkflowRequest request, CancellationToken cancellationToken = default);
}
