using Newtonsoft.Json.Linq;

namespace Ballware.Llm.Embedding;

public sealed record Chunk(
    string Text,
    JObject Metadata
);

public sealed record ChunkRequest(
    Guid originId,
    string ContentType,
    int MaxTokensPerChunk = 750,
    int OverlapTokens = 120
);


public interface IChunker<in TInput>
{
    IReadOnlyList<Chunk> Chunk(TInput input, ChunkRequest request);
}