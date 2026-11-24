namespace Ballware.Llm.Data.Public;

public class ChatSessionMessage
{
    public string Role { get; set; }

    public string Content { get; set; }

    public int Sequence { get; set; }

    public DateTime? CreateStamp { get; set; }
}
