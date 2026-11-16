namespace Ballware.Llm.Inference;

public sealed record ChatMessage(ChatRole Role, string Content);
