using Ballware.Llm.Tools.Embedding.Internal;

namespace Ballware.Llm.Tools.Embedding;

public static class ToolRegistryExtensions
{
    public static IToolRegistry RegisterBallwareLlmToolsForEmbeddings(
        this IToolRegistry registry)
    {
        registry.RegisterScopedToolProvider<EmbeddingsToolProvider>();
        
        return registry;
    }
}
