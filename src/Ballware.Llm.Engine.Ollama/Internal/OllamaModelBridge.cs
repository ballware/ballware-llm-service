using Ballware.Llm.Engine.Ollama.Configuration;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Tools;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class OllamaModelBridge : IOllamaModelBridge
{
    private OllamaOptions Options { get; }
    private OllamaToolRegistry ToolRegistry { get; }
    private IOllamaApiClient InferenceApiClient { get; }
    private IOllamaApiClient EmbeddingApiClient { get; }
    
    public OllamaModelBridge(OllamaOptions options, OllamaToolRegistry toolRegistry)
    {
        Options = options;
        ToolRegistry = toolRegistry;
        
        InferenceApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.InferenceModel
        };
        
        EmbeddingApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.EmbeddingModel
        };
    }

    public IOllamaChat StartChat(OllamaChatOptions options, Guid tenantId, Guid userId, IDictionary<string, object> claims)
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
        chat.ToolInvoker = new DefaultToolInvoker();
        
        return new OllamaChat(chat, ToolRegistry.GetToolsForUser(tenantId, userId, claims));
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