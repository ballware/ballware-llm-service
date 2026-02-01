using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Api.Model;

public class OriginAddFromAttachmentRequest
{
    [Required]
    public required string OwnerEntity { get; set; }
    
    [Required]
    public required Guid OwnerId { get; set; }
    
    [Required]
    public required Guid AttachmentId { get; set; }
}