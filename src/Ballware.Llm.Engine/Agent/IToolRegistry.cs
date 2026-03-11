namespace Ballware.Llm.Engine.Agent;

public interface IToolRegistry
{
    void RegisterToolProvider(IToolProvider toolProvider);
    void RegisterMcpService(string identifier);
}