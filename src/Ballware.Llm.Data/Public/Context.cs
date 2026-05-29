using Ballware.Shared.Data.Public;

namespace Ballware.Llm.Data.Public;

public class Context : IEditable
{
    public Guid Id { get; set; }

    public string Identifier { get; set; }
    
    public string? SystemPromptTemplate { get; set; }
    public string? WelcomeMessage { get; set; }
    
    public bool Seed { get; set; }

    public string? AssistantStagesJson { get; set; }

    public int? NumCtx { get; set; }
    
    public string? Model { get; set; }
}
