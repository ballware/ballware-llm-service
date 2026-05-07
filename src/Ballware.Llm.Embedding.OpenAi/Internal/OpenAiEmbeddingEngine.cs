using System.ClientModel;
using Ballware.Llm.Embedding.OpenAi.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;

namespace Ballware.Llm.Embedding.OpenAi.Internal;

internal class OpenAiEmbeddingEngine(OpenAiEmbeddingOptions options) : IEmbeddingEngine
{
    private IEmbeddingGenerator<string, Embedding<float>> EmbeddingApiClient { get; } = new EmbeddingClient(options.Model, BuildOpenAiCredential(options.ApiKey), new OpenAIClientOptions()
        {
            Endpoint = BuildOpenAiEndpoint(options.Endpoint)
        })
        .AsIEmbeddingGenerator();

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
    
    public async Task<float[]> GetEmbeddingAsync(string chunk, string? model = null, CancellationToken cancellationToken = default)
    {
        var embeddingOptions = new Microsoft.Extensions.AI.EmbeddingGenerationOptions()
        {
            ModelId = model ?? options.Model
        };
        
        var vector = await EmbeddingApiClient.GenerateVectorAsync(chunk, embeddingOptions, cancellationToken);
        
        return vector.ToArray();
    }
}