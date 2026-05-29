namespace Ballware.Llm.Service.Configuration;

public class AssistantContextDefaultsOptions
{
    public string? StructuredStageInstructionsTemplate { get; set; }

    public string? KnowledgeStageInstructionsTemplate { get; set; }

    public string? InternetStageInstructionsTemplate { get; set; }

    public string? InternetQueryPreparationInstructionsTemplate { get; set; }

    public string? InternetQueryPreparationPromptTemplate { get; set; }

    public string? ConversationPromptTemplate { get; set; }

    public string? InternetPromptTemplate { get; set; }
}
