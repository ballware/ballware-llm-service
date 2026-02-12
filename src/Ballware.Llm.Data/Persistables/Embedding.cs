using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Llm.Data.Persistables;

[Table("embedding")]
public class Embedding : IEntity, ITenantable, IAuditable
{
    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }

    [Column("tenant_id")]
    public virtual Guid TenantId { get; set; }
    
    [Column("origin_id")]
    [Required]
    public virtual Guid OriginId { get; set; }
    
    [Column("model")]
    [Required]
    public virtual string? Model { get; set; }
    
    [Column("active")]
    public virtual bool Active { get; set; }
    
    [Column("top_k")]
    public int? TopK { get; set;  }
    
    [Column("max_tokens_per_chunk")]
    public int? MaxTokensPerChunk { get; set; }
    
    [Column("overlap_tokens")]
    public int? OverlapTokens { get; set; }
    
    [Column("creator_id")]
    public virtual Guid? CreatorId { get; set; }

    [Column("create_stamp")]
    public virtual DateTime? CreateStamp { get; set; }

    [Column("last_changer_id")]
    public virtual Guid? LastChangerId { get; set; }

    [Column("last_change_stamp")]
    public virtual DateTime? LastChangeStamp { get; set; }
}