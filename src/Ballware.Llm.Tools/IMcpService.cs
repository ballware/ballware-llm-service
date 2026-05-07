namespace Ballware.Llm.Tools;

public interface IMcpService
{
    public string Identifier { get; }
    public bool UserTokenDelegation { get; }
}