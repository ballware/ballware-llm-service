namespace Ballware.Llm.Inference;

public class ChatContextOptions
{
    public string? Model { get; set; }
    public int? NumCtx { get; set; }
    public string? SystemPrompt { get; set; }
}