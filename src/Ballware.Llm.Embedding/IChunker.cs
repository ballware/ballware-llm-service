using Newtonsoft.Json.Linq;

namespace Ballware.Llm.Embedding;

public sealed record Chunk(
    string Text,
    JObject Metadata
);

public sealed record ChunkRequest(
    Guid OriginId,
    int? MaxTokensPerChunk,
    int? OverlapTokens
);


public interface IChunker<in TInput>
{
    IReadOnlyList<Chunk> Chunk(TInput input, ChunkRequest request);
}