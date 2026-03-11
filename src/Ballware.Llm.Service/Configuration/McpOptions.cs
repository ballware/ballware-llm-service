namespace Ballware.Llm.Service.Configuration;

public class McpAgentOptions : ServiceClientOptions
{
    public required string Namespace { get; set; }
}

public class McpOptions
{
    public List<McpAgentOptions> Agents { get; set; } = [];
}