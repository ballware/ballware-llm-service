using System.Threading.Tasks;
using Ballware.Llm.Ollama.Configuration;
using OllamaSharp;
using OllamaSharp.Models;

namespace Ballware.Llm.Ollama.Internal;

internal class OllamaModelBridge : IOllamaModelBridge
{
    private OllamaOptions Options { get; }
    private IOllamaApiClient InferenceApiClient { get; }
    private IOllamaApiClient EmbeddingApiClient { get; }
    
    public OllamaModelBridge(OllamaOptions options)
    {
        Options = options;
        
        InferenceApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.InferenceModel
        };
        
        EmbeddingApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.EmbeddingModel
        };
    }

    public IOllamaChat StartChat(OllamaChatOptions options)
    {
        var requestOptions = new RequestOptions
        {
            NumCtx = options.NumCtx ?? 8192
        };
        
        var chat = options.SystemPrompt != null 
            ? new Chat(InferenceApiClient, options.SystemPrompt) 
            : new Chat(InferenceApiClient);
        
        chat.Model = options.Model ?? Options.InferenceModel;
        chat.Options = requestOptions;
        
        return new OllamaChat(chat);
    }

    public async Task<float[]> EmbedAsync(string chunk, string? model)
    {
        var embedResponse = await EmbeddingApiClient.EmbedAsync(new EmbedRequest()
        {   
            Model = model ?? Options.EmbeddingModel,
            Input = [chunk]
        });

        return embedResponse.Embeddings[0];
    }
}