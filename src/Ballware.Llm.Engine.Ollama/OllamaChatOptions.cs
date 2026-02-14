namespace Ballware.Llm.Engine.Ollama;

public class OllamaChatOptions
{
    public string? Model { get; set; }
    public int? NumCtx { get; set; }
    public string? SystemPrompt { get; set; }
}