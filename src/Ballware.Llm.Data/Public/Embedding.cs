using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Embedding: IEditable
{
    public Guid Id { get; set; }
    public Guid OriginId { get; set; }
    public string? Model { get; set; }
    public bool Active { get; set; }
    public int? TopK { get; set; }
    public int? MaxTokensPerChunk { get; set; }
    public int? OverlapTokens { get; set; }
}