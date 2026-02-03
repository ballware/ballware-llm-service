using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;
using Pgvector;

namespace Ballware.Llm.Data.Persistables;

[Table("chunk")]
public class Chunk : IEntity, ITenantable, IAuditable
{
    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }
    
    [Column("embedding_id")]
    public virtual Guid EmbeddingId { get; set; }

    [Column("text")]
    [MaxLength(102400)]
    public virtual string Text { get; set; }
    
    [Column("data", TypeName = "vector(768)")]
    public virtual Vector Data { get; set; } = default!;

    [Column("tenant_id")]
    public virtual Guid TenantId { get; set; }
    
    [Column("creator_id")]
    public virtual Guid? CreatorId { get; set; }

    [Column("create_stamp")]
    public virtual DateTime? CreateStamp { get; set; }

    [Column("last_changer_id")]
    public virtual Guid? LastChangerId { get; set; }

    [Column("last_change_stamp")]
    public virtual DateTime? LastChangeStamp { get; set; }
}