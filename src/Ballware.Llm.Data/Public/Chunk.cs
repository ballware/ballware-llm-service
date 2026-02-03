using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Chunk : IEditable
{
    public Guid Id { get; set; }
    public Guid EmbeddingId { get; set; }
    public string Text { get; set; }
    public float[] Data { get; set; }
}