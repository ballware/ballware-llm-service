using Ballware.Llm.Ollama.Configuration;
using OllamaSharp;

namespace Ballware.Llm.Ollama.Internal;

internal class OllamaModelBridge : IOllamaModelBridge
{
    private OllamaOptions Options { get; }
    private IOllamaApiClient ApiClient { get; }
    
    public OllamaModelBridge(OllamaOptions options)
    {
        Options = options;
        ApiClient = new OllamaApiClient(Options.Url);
        ApiClient.SelectedModel = Options.Model;
    }

    public IOllamaChat StartChat()
    {
        return new OllamaChat(new Chat(ApiClient));
    }
}