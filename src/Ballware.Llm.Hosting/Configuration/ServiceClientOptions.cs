using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Hosting.Configuration;

public class ServiceClientOptions
{
    [Required]
    public required string ServiceUrl { get; set; }

    public bool ForwardUserIdentity { get; set; } = false;

    public string? CredentialIdentifier { get; set; }
}
