using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Image : IEditable
{
    public Guid Id { get; set; }
    public Guid OriginId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Data { get; set; } = [];
}
