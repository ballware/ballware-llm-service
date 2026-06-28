using System.Text.Json.Serialization;

namespace Ballware.Llm.Api.Model;

public class OpenAiChatCompletionStreamResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public IList<OpenAiChatCompletionStreamChoice> Choices { get; set; } = new List<OpenAiChatCompletionStreamChoice>();
}

public class OpenAiChatCompletionStreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiChatCompletionMessage Delta { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}
