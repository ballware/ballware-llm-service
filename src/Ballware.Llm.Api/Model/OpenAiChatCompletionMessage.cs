using System.Text.Json.Serialization;

namespace Ballware.Llm.Api.Model;

public class OpenAiChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
