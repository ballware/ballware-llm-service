using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ballware.Shared.Data.Persistables;

namespace Ballware.Llm.Data.Persistables;

[Table("context")]
public class Context : IEntity, ITenantable, IAuditable
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
    
    [Column("identifier")]
    [Required]
    [MaxLength(100)]
    public string Identifier { get; set; }
    
    [Column("system_prompt_template")]
    public string? SystemPromptTemplate { get; set; }
    
    [Column("welcome_message")]
    public string? WelcomeMessage { get; set; }
    
    [Column("num_ctx")]
    public int? NumCtx { get; set; }
    
    [Column("model")]
    [MaxLength(100)]
    public string? Model { get; set; }
}