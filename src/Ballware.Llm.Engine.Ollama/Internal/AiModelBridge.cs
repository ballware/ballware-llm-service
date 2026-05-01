using Ballware.Llm.Engine.Ollama.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal class AiModelBridge : IAiModelBridge
{
    private const string OllamaApiKey = "ollama";
    
    private OllamaOptions Options { get; }
    private AiToolRegistry ToolRegistry { get; }
    private IChatClient InferenceApiClient { get; }
    private IEmbeddingGenerator<string, Embedding<float>> EmbeddingApiClient { get; }
    private ILoggerFactory LoggerFactory { get; }
    
    public AiModelBridge(OllamaOptions options, AiToolRegistry toolRegistry, ILoggerFactory loggerFactory)
    {
        Options = options;
        ToolRegistry = toolRegistry;
        LoggerFactory = loggerFactory;

        var clientOptions = new OpenAIClientOptions()
        {
            Endpoint = BuildOpenAiEndpoint(Options.Url)
        };

        var credential = new ApiKeyCredential(OllamaApiKey);

        InferenceApiClient = new ChatClient(Options.InferenceModel, credential, clientOptions)
            .AsIChatClient();

        EmbeddingApiClient = new EmbeddingClient(Options.EmbeddingModel, credential, clientOptions)
            .AsIEmbeddingGenerator();
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
        
        tools.AddRange(ToolRegistry.GetToolsForUser(serviceProvider, tenantId, userId, claims));
        
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        
        foreach (var mcpService in ToolRegistry.GetMcpServices())
        {
            var httpClient = httpClientFactory?.CreateClient(mcpService.Identifier);
            
            if (httpClient != null)
            {
                var transportOptions = new HttpClientTransportOptions()
                {
                    Endpoint = httpClient.BaseAddress ?? new Uri("/"),
                };
                
                if (mcpService.UserTokenDelegation && !string.IsNullOrEmpty(userAccessToken))
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
        
        return new AiChat(chat, chatOptions, LoggerFactory.CreateLogger<AiChat>());
    }

    public async Task<float[]> EmbedAsync(string chunk, string? model)
    {
        var embeddingOptions = new Microsoft.Extensions.AI.EmbeddingGenerationOptions()
        {
            ModelId = model ?? Options.EmbeddingModel
        };
        
        var vector = await EmbeddingApiClient.GenerateVectorAsync(chunk, embeddingOptions);
        
        return vector.ToArray();
    }

    private static Uri BuildOpenAiEndpoint(string baseUrl)
    {
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var currentPath = baseUri.AbsolutePath.TrimEnd('/');
        
        if (string.IsNullOrEmpty(currentPath) || currentPath == "/")
        {
            return new Uri(baseUri, "v1");
        }

        if (currentPath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return baseUri;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{currentPath}/v1"
        };

        return builder.Uri;
    }
}
