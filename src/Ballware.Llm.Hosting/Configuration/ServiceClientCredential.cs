using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Hosting.Configuration;

public class ServiceClientCredential
{
    [Required]
    public required string Identifier { get; set; }

    [Required]
    public required string TokenEndpoint { get; set; }

    [Required]
    public required string ClientId { get; set; }

    [Required]
    public required string ClientSecret { get; set; }

    [Required]
    public required string Scopes { get; set; }
}
