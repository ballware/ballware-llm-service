using Ballware.Llm.Service.Configuration;
using System.Text.Json;
using Ballware.Assistant.Workflow;
using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Service.Extensions;

public class ContextDefaultsRepositoryHook(AssistantContextDefaultsOptions options)
    : ITenantableRepositoryHook<Ballware.Llm.Data.Public.Context, Ballware.Llm.Data.Persistables.Context>
{
    public Data.Public.Context ExtendById(Guid tenantId, string identifier, IDictionary<string, object> claims, Data.Public.Context value)
    {
        return value;
    }

    public void BeforeSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, Data.Public.Context value,
        bool insert)
    {
        if (!insert)
        {
            return;
        }

        if (!value.Seed)
        {
            return;
        }

        var stages = new List<AssistantWorkflowStageDefinition>
        {
            new()
            {
                Type = AssistantWorkflowStageType.StructuredData,
                Enabled = true,
                InstructionsTemplate = options.StructuredStageInstructionsTemplate,
                PromptTemplate = options.ConversationPromptTemplate
            },
            new()
            {
                Type = AssistantWorkflowStageType.KnowledgeBase,
                Enabled = true,
                InstructionsTemplate = options.KnowledgeStageInstructionsTemplate,
                PromptTemplate = options.ConversationPromptTemplate
            },
            new()
            {
                Type = AssistantWorkflowStageType.InternetResearch,
                Enabled = true,
                InstructionsTemplate = options.InternetStageInstructionsTemplate,
                PromptTemplate = options.InternetPromptTemplate
            }
        };

        value.AssistantStagesJson = JsonSerializer.Serialize(stages);
    }

    public void BeforeRemove(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        Data.Persistables.Context persistable)
    {
    }

}
