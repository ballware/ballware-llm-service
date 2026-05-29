using Ballware.Assistant;
using Ballware.Assistant.Workflow.MsAgentNetwork.Configuration;

namespace Ballware.Assistant.Workflow.MsAgentNetwork.Internal;

internal sealed class MsAgentNetworkAssistantSessionFactory(
    IAssistantWorkflow workflow,
    MsAgentNetworkWorkflowOptions workflowOptions) : IAssistantSessionFactory
{
    public Task<IAssistantSession> StartSessionAsync(
        AssistantSessionStartOptions startOptions,
        CancellationToken cancellationToken = default)
    {
        var context = new AssistantWorkflowContext
        {
            TenantId = startOptions.TenantId,
            UserId = startOptions.UserId,
            Claims = startOptions.Claims,
            UserAccessToken = startOptions.UserAccessToken,
            Model = startOptions.Model,
            NumCtx = startOptions.NumCtx,
            SystemPrompt = startOptions.SystemPrompt,
            Stages = startOptions.Stages
        };

        IAssistantSession session = new MsAgentNetworkAssistantSession(workflow, context, workflowOptions.MaxHistoryEntries);
        return Task.FromResult(session);
    }
}
