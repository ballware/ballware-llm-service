using System.Text.Json.Serialization;

namespace Ballware.Llm.Api.Model;

public class OpenAiChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public IList<OpenAiChatCompletionMessage> Messages { get; set; } = new List<OpenAiChatCompletionMessage>();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
