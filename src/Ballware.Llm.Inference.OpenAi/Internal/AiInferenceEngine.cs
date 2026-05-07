using System.ClientModel;
using Ballware.Llm.Inference.OpenAi.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace Ballware.Llm.Inference.OpenAi.Internal;

internal class AiInferenceEngine(ILoggerFactory loggerFactory, OpenAiInferenceOptions options, DefaultToolRegistry toolRegistry) : IInferenceEngine
{
    private IChatClient InferenceApiClient { get; } = new ChatClient(options.Model, BuildOpenAiCredential(options.ApiKey), new OpenAIClientOptions()
        {
            Endpoint = BuildOpenAiEndpoint(options.Endpoint)
        }).AsIChatClient();
    
    private static ApiKeyCredential BuildOpenAiCredential(string apiKey)
    {
        return new ApiKeyCredential(apiKey);
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
    
    public async Task<IChatSession> StartChatAsync(IServiceProvider serviceProvider, ChatContextOptions contextOptions, Guid tenantId,
        Guid userId, IDictionary<string, object> claims, string? userAccessToken)
    {
        var chat = new ChatClientBuilder(InferenceApiClient)
            .UseFunctionInvocation()
            .Build();

        var chatOptions = new ChatOptions();

        chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary();
        chatOptions.ModelId = contextOptions.Model ?? options.Model;

        if (contextOptions.NumCtx != null)
        {
            chatOptions.AdditionalProperties.Add("num_ctx", contextOptions.NumCtx);
        }
        
        var tools = new List<AITool>();
        
        tools.AddRange(toolRegistry.GetToolsForUser(serviceProvider, tenantId, userId, claims));
        
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        
        foreach (var mcpService in toolRegistry.GetMcpServices())
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

        if (!string.IsNullOrEmpty(contextOptions.SystemPrompt))
        {
            chatOptions.Instructions = contextOptions.SystemPrompt;
        }

        return new AiChatSession(
            chat,
            chatOptions,
            tools,
            loggerFactory.CreateLogger<AiChatSession>());
    }
}