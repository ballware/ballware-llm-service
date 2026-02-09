namespace Ballware.Llm.Api.Model;

public class ContextSimulateSystemPromptRequest
{
    public required string ContextIdentifier { get; set; }
    public required string InitialPrompt { get; set; }
}