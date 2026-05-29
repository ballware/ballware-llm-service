namespace Ballware.Assistant.Workflow.MsAgentNetwork.Configuration;

public class MsAgentNetworkWorkflowOptions
{
    public int MaxHistoryEntries { get; set; } = 16;

    public List<string> KnowledgeToolNamePrefixes { get; set; } = ["origins."];

    public List<string> InternetMcpServiceIdentifiers { get; set; } = [];
}
