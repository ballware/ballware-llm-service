using System.ComponentModel.DataAnnotations;
using Ballware.Llm.Tools;

namespace Ballware.Llm.Service.Configuration;

public class McpService(string identifier, bool userTokenDelegation) : IMcpService
{
    public string Identifier { get; } = identifier;
    public bool UserTokenDelegation { get; } = userTokenDelegation;  
}

public class McpAgentOptions
{
    [Required]
    public required string Namespace { get; set; }
    
    [Required]
    public required string ServiceUrl { get; set; }
    
    public bool UserTokenDelegation { get; set; } = false;
    
    public string? CredentialIdentifier { get; set; }
}

public class McpOptions
{
    public List<McpAgentOptions> Agents { get; set; } = [];
}