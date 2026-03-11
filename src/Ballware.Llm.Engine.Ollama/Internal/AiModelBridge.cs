using Ballware.Llm.Engine.Ollama.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OllamaSharp;
using OllamaSharp.Models;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class AiModelBridge : IAiModelBridge
{
    private OllamaOptions Options { get; }
    private AiToolRegistry ToolRegistry { get; }
    private IChatClient InferenceApiClient { get; }
    private IOllamaApiClient EmbeddingApiClient { get; }
    
    public AiModelBridge(OllamaOptions options, AiToolRegistry toolRegistry)
    {
        Options = options;
        ToolRegistry = toolRegistry;
        
        InferenceApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.InferenceModel
        };

        EmbeddingApiClient = new OllamaApiClient(Options.Url)
        {
            SelectedModel = Options.EmbeddingModel,
        };
    }

    public async Task<IAiChat> StartChatAsync(IServiceProvider serviceProvider, AiChatOptions options, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken)
    {
        var chat = new ChatClientBuilder(InferenceApiClient)
            .UseFunctionInvocation()
            .Build();

        var chatOptions = new ChatOptions();

        chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary();
        chatOptions.ModelId = options.Model ?? Options.InferenceModel;

        if (options.NumCtx != null)
        {
            chatOptions.AdditionalProperties.Add("num_ctx", options.NumCtx);
        }
        
        var tools = new List<AITool>();
        
        tools.AddRange(ToolRegistry.GetToolsForUser(tenantId, userId, claims));
        
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        
        foreach (var mcpIdentifier in ToolRegistry.GetMcpServiceIdentifiers())
        {
            var httpClient = httpClientFactory?.CreateClient(mcpIdentifier);
            
            if (httpClient != null)
            {
                var transportOptions = new HttpClientTransportOptions()
                {
                    Endpoint = httpClient.BaseAddress ?? new Uri("/"),
                };
                
                if (!string.IsNullOrEmpty(userAccessToken))
                {
                    if (transportOptions.AdditionalHeaders == null)
                    {
                        transportOptions.AdditionalHeaders = new Dictionary<string, string>();
                    }
                    
                    transportOptions.AdditionalHeaders.Add("Authorization", $"Bearer {userAccessToken}");
                }
                
                var transport = new HttpClientTransport(transportOptions, httpClient);

                var mcpClient = await McpClient.CreateAsync(transport);

                var mcpClientTools = await mcpClient.ListToolsAsync();
                
                tools.AddRange(mcpClientTools);
            }
        }

        chatOptions.Tools = tools;

        if (!string.IsNullOrEmpty(options.SystemPrompt))
        {
            chatOptions.Instructions = options.SystemPrompt;
        }
        
        return new AiChat(chat, chatOptions);
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