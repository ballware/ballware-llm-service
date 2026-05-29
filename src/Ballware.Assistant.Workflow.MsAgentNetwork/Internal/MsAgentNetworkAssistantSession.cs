using Ballware.Assistant;

namespace Ballware.Assistant.Workflow.MsAgentNetwork.Internal;

internal sealed class MsAgentNetworkAssistantSession(
    IAssistantWorkflow workflow,
    AssistantWorkflowContext context,
    int maxHistoryEntries) : IAssistantSession
{
    private readonly List<AssistantConversationEntry> _history = [];

    public async Task PromptAsync(string message, Action<string> tokenHandler)
    {
        var history = _history.Count <= maxHistoryEntries
            ? _history.ToList()
            : _history.TakeLast(maxHistoryEntries).ToList();

        var response = await workflow.ExecuteAsync(context, new AssistantWorkflowRequest
        {
            Message = message,
            History = history
        });

        _history.Add(new AssistantConversationEntry("user", message));
        _history.Add(new AssistantConversationEntry("assistant", response.Message));

        tokenHandler(response.Message);
    }
}
