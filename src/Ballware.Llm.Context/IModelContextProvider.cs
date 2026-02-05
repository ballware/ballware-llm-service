namespace Ballware.Llm.Context;

public interface IModelContextProvider
{
    Task<string> ProvideWelcomeMessageAsync(Guid tenantId, string contextIdentifier);
    Task<string> ProvideSystemPromptAsync(Guid tenantId, string contextIdentifier, string initialPrompt);
}