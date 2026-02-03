using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Llm.Data.Persistables;

[Table("origin")]
public class Origin : IEntity, ITenantable, IAuditable
{
    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }

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
    
    [Column("name")]
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    
    [Column("description")]
    public string Description { get; set; }
    
    [Column("global_context")]
    public bool GlobalContext { get; set; }
    
    [Column("owner_entity")]
    [Required]
    [MaxLength(100)]
    public virtual string OwnerEntity { get; set; }
    
    [Column("owner_id")]
    [Required]
    public virtual Guid OwnerId { get; set; }
    
    [Column("refering_entity")]
    [Required]
    [MaxLength(100)]
    public virtual string ReferingEntity { get; set; }
    
    [Column("refering_id")]
    [Required]
    public virtual Guid ReferingId { get; set; }
    
    [Column("content_type")]
    [Required]
    [MaxLength(100)]
    public virtual string ContentType { get; set; }
}