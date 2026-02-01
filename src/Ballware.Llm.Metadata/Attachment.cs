namespace Ballware.Llm.Metadata;

public class Attachment
{
    public required string OwnerEntity { get; set; }
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ContentType { get; set; }
}