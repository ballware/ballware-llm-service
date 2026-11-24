using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Llm.Data.Persistables;

[Table("chat_message")]
public class ChatMessage : IEntity, ITenantable, IAuditable
{
    public const string RoleSystem = "system";
    public const string RoleUser = "user";
    public const string RoleAssistant = "assistant";

    [Column("id")]
    public virtual long? Id { get; set; }

    [Column("uuid")]
    public virtual Guid Uuid { get; set; }

    [Column("tenant_id")]
    public virtual Guid TenantId { get; set; }

    [Column("session_id")]
    public virtual Guid SessionId { get; set; }

    [Column("role")]
    [Required]
    [MaxLength(20)]
    public virtual string Role { get; set; }

    [Column("content")]
    public virtual string Content { get; set; }

    [Column("sequence")]
    public virtual int Sequence { get; set; }

    [Column("creator_id")]
    public virtual Guid? CreatorId { get; set; }

    [Column("create_stamp")]
    public virtual DateTime? CreateStamp { get; set; }

    [Column("last_changer_id")]
    public virtual Guid? LastChangerId { get; set; }

    [Column("last_change_stamp")]
    public virtual DateTime? LastChangeStamp { get; set; }
}
