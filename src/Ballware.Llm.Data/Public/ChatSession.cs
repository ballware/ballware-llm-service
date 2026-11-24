using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class ChatSession : IEditable
{
    public Guid Id { get; set; }

    public string ContextIdentifier { get; set; }

    public string? Title { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateTime? CreateStamp { get; set; }

    public DateTime? LastChangeStamp { get; set; }

    public List<ChatSessionMessage>? Messages { get; set; }
}
