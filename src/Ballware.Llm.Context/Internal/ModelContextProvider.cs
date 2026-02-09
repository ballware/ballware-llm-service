using Ballware.Llm.Context.Configuration;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Ollama;
using SmartFormat;

namespace Ballware.Llm.Context.Internal;

internal class ModelContextProvider(ContextOptions options, IOllamaModelBridge modelBridge, IContextRepository contextRepository, IOriginRepository originRepository, IEmbeddingRepository embeddingRepository, IChunkRepository chunkRepository) : IModelContextProvider
{
    public async Task<string> ProvideWelcomeMessageAsync(Guid tenantId, string contextIdentifier)
    {
        var context = await contextRepository.GetByIdentifierAsync(tenantId, contextIdentifier);

        return context?.WelcomeMessage ?? "";
    }

    public async Task<string> ProvideSystemPromptAsync(Guid tenantId, string contextIdentifier, string initialPrompt)
    {
        var context = await contextRepository.GetByIdentifierAsync(tenantId, contextIdentifier);

        if (context != null && !string.IsNullOrEmpty(context.SystemPromptTemplate))
        {
            var systemContextBuilder = new System.Text.StringBuilder();
            
            var globalOrigins = await originRepository.GetGlobalOriginsAsync(tenantId);

            foreach (var origin in globalOrigins.OrderBy(o => (o.GlobalRelevance ?? 0)))
            {
                var activeEmbeddings =
                    (await embeddingRepository.GetActiveEmbeddingsForOrigin(tenantId, origin.Id)).ToList();
                
                if (activeEmbeddings.Any())
                {
                    systemContextBuilder.AppendLine("Source: " + origin.Name);
                    systemContextBuilder.AppendLine("Contents:");
                    
                    foreach (var embedding in activeEmbeddings)
                    {
                        var initialPromptVector = await modelBridge.EmbedAsync(initialPrompt, embedding.Model);
                        
                        var topChunks = await chunkRepository.FetchTopKSimilarChunksAsync(tenantId, embedding.Id, initialPromptVector, embedding.TopK ?? options.DefaultTopK);

                        foreach (var chunk in topChunks)
                        {
                            systemContextBuilder.AppendLine(chunk);
                        }
                    }
                    
                    systemContextBuilder.AppendLine();
                }
            }
            
            var systemPrompt = Smart.Format(context.SystemPromptTemplate, new
            {
               GlobalContext = systemContextBuilder.ToString(),
               InitialPrompt = initialPrompt
            });
            
            return systemPrompt;
        }

        return string.Empty;
    }

    public Task<long> EstimateTokenCountAsync(Guid tenantId, string contextIdentifier, string systemPrompt)
    {
        return Task.FromResult(systemPrompt.Length / 4L);
    }

    public async Task<ModelContextOptions> ProvideOptionsAsync(Guid tenantId, string contextIdentifier)
    {
        var context = await contextRepository.GetByIdentifierAsync(tenantId, contextIdentifier);

        if (context != null)
        {
            return new ModelContextOptions()
            {
                Model = context.Model,
                NumCtx = context.NumCtx
            };
        }

        return new ModelContextOptions();
    }
}