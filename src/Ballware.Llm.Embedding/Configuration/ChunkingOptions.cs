namespace Ballware.Llm.Embedding.Configuration;

public class ChunkingOptions
{
    public int DefaultMaxTokensPerChunk { get; set; } = 500;
    public int DefaultOverlapTokens { get; set; } = 50;
}