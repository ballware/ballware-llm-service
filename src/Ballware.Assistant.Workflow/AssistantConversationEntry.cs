namespace Ballware.Assistant.Workflow;

public sealed class AssistantConversationEntry
{
    public AssistantConversationEntry(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; }

    public string Content { get; }
}
