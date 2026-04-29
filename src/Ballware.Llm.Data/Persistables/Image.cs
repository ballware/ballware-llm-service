using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;
using Pgvector;

namespace Ballware.Llm.Data.Persistables;

[Table("image")]
public class Image : IEntity, ITenantable, IAuditable
{
    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }

    [Column("origin_id")]
    [Required]
    public virtual Guid OriginId { get; set; }

    [Column("name")]
    [Required]
    [MaxLength(512)]
    public virtual string Name { get; set; } = string.Empty;

    [Column("description")]
    [MaxLength(4096)]
    public virtual string Description { get; set; } = string.Empty;

    [Column("content_type")]
    [Required]
    [MaxLength(100)]
    public virtual string ContentType { get; set; } = string.Empty;

    [Column("text")]
    [Required]
    [MaxLength(16384)]
    public virtual string Text { get; set; } = string.Empty;

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
