namespace Ballware.Llm.Engine.Agent;

public interface IMcpService
{
    public string Identifier { get; }
    public bool UserTokenDelegation { get; }
}