namespace Ballware.Llm.Context;

public interface IModelContextProvider
{
    Task<string> ProvideWelcomeMessageAsync(Guid tenantId, string contextIdentifier);
    Task<string> ProvideSystemPromptAsync(Guid tenantId, string contextIdentifier, string initialPrompt);
    Task<long> EstimateTokenCountAsync(Guid tenantId, string contextIdentifier, string systemPrompt);
    Task<ModelContextOptions> ProvideOptionsAsync(Guid tenantId, string contextIdentifier);
}