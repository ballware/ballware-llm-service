namespace Ballware.Llm.Tools;

public interface IToolRegistry
{
    IReadOnlyList<IToolProvider> StaticToolProviders { get; }

    IReadOnlyList<Type> ScopedToolProviderTypes { get; }

    IReadOnlyList<IMcpService> McpServices { get; }

    void RegisterStaticToolProvider(IToolProvider toolProvider);
    
    void RegisterScopedToolProvider<T>() where T : IToolProvider;
    
    void RegisterMcpService(IMcpService mcpService);
}
