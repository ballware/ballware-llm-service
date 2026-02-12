namespace Ballware.Llm.Api.Model;

public class OriginCreateEmbeddingRequest
{
    public Guid[] Ids { get; set; }
    public required string Model { get; set; }
    public bool Activate { get; set; }
    public int? TopK { get; set; }
    public int? TokensPerChunk { get; set; }
    public int? OverlapTokens { get; set; }
}