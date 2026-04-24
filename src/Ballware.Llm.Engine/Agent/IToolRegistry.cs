namespace Ballware.Llm.Engine.Agent;

public interface IToolRegistry
{
    void RegisterStaticToolProvider(IToolProvider toolProvider);
    
    void RegisterScopedToolProvider<T>() where T : IToolProvider;
    
    void RegisterMcpService(IMcpService mcpService);
}