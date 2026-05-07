namespace Ballware.Llm.Inference;

public interface IChatSessionBuilder
{
    IChatSessionBuilder WithInstructions(string initialInstructions);
    IChatSessionBuilder WithModel(string model);
    IChatSessionBuilder WithAdditionalStage(IChatSessionBuilderStage stage);
    IChatSession Build();
}