using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Embedding: IEditable
{
    public Guid Id { get; set; }
    public Guid OriginId { get; set; }
    public string? Model { get; set; }
    public bool Active { get; set; }
}