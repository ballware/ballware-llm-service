using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Llm.Data.Persistables;

[Table("chat_session")]
public class ChatSession : IEntity, ITenantable, IAuditable
{
    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }

    [Column("tenant_id")]
    public virtual Guid TenantId { get; set; }

    [Column("user_id")]
    public virtual Guid UserId { get; set; }

    [Column("context_identifier")]
    [Required]
    [MaxLength(100)]
    public virtual string ContextIdentifier { get; set; }

    [Column("title")]
    [MaxLength(200)]
    public virtual string? Title { get; set; }

    [Column("creator_id")]
    public virtual Guid? CreatorId { get; set; }

    [Column("create_stamp")]
    public virtual DateTime? CreateStamp { get; set; }

    [Column("last_changer_id")]
    public virtual Guid? LastChangerId { get; set; }

    [Column("last_change_stamp")]
    public virtual DateTime? LastChangeStamp { get; set; }
}
