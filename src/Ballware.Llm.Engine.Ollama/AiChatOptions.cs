namespace Ballware.Llm.Engine.Ollama;

public class AiChatOptions
{
    public string? Model { get; set; }
    public int? NumCtx { get; set; }
    public string? SystemPrompt { get; set; }
}