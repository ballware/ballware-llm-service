namespace Ballware.Llm.Api.Model;

public class ContextSimulateSystemPromptResponse
{
    public required string SystemPrompt { get; set; }
    public long EstimatedTokenCount { get; set; }
    
}