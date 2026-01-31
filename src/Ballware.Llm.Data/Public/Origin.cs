using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Origin : IEditable
{
    public Guid Id { get; set; }
    
    public string OwnerEntity { get; set; }
    public Guid OwnerId { get; set; }
    
    public string ReferingEntity { get; set; }
    public Guid ReferingId { get; set; }
    
    public string Name { get; set; }
    public string Description { get; set; }
    public string Payload { get; set; }
}