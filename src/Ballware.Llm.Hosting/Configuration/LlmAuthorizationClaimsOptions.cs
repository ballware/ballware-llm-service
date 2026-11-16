using System.IdentityModel.Tokens.Jwt;

namespace Ballware.Llm.Hosting.Configuration;

public class LlmAuthorizationClaimsOptions
{
    public string TenantClaim { get; set; } = "tenant";
    public string UserIdClaim { get; set; } = JwtRegisteredClaimNames.Sub;
    public string RightClaim { get; set; } = "right";
}
