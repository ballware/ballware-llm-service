using System.Collections.Immutable;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Embedding;
using Ballware.Llm.Ingestion;
using Ballware.Llm.Engine.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Ballware.Llm.Jobs.Internal;

public class OriginCreateEmbeddingJob(IServiceProvider serviceProvider, IOriginRepository originRepository, IEmbeddingRepository embeddingRepository, IChunkRepository chunkRepository, ChunkerRegistry chunkerRegistry, IOllamaModelBridge modelBridge) : IJob
{
    public static readonly JobKey Key = new JobKey("createembedding", "origin");

    public async Task Execute(IJobExecutionContext context)
    {
        var tenantId = context.MergedJobDataMap.GetGuidValue("tenantId");
        var userId = context.MergedJobDataMap.GetGuidValue("userId");
        var originId = context.MergedJobDataMap.GetGuidValue("id");
        var model = context.MergedJobDataMap.GetString("model");
        var activate =  context.MergedJobDataMap.GetBooleanValue("activate");
        
        int? maxTokensPerChunk = null;
        int? overlapTokens = null;

        if (context.MergedJobDataMap.TryGetIntValue("tokensPerChunk", out var maxTokensPerChunkInt))
        {
            maxTokensPerChunk = maxTokensPerChunkInt;
        }

        if (context.MergedJobDataMap.TryGetIntValue("overlapTokens", out var overlapTokensInt))
        {
            overlapTokens = overlapTokensInt;
        }

        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }
        
        var origin = await originRepository.ByIdAsync(tenantId, "primary", ImmutableDictionary<string, object>.Empty, originId);

        if (origin == null)
        {
            throw new ArgumentNullException(nameof(origin));
        }

        var chunkerType = chunkerRegistry.GetChunker(origin.ContentType);

        if (chunkerType == null)
        {
            throw new ArgumentNullException(nameof(chunkerType));
        }

        var chunker = (IChunker<string>)serviceProvider.GetRequiredService(chunkerType);

        var chunks = chunker.Chunk(origin.Content, new ChunkRequest(origin.Id, maxTokensPerChunk, overlapTokens));

        var embedding = new Data.Public.Embedding()
        {
            Id = Guid.NewGuid(),
            OriginId = origin.Id,
            Model = model,
            Active = activate,
        };

        await embeddingRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty,
            embedding);
        
        foreach (var c in chunks)
        {
            var chunkEmbedding = await modelBridge.EmbedAsync(c.Text, model);
            
            var chunk = new Data.Public.Chunk()
            {
                Id = Guid.NewGuid(),
                EmbeddingId = embedding.Id,
                Text = c.Text,
                Data = chunkEmbedding.ToArray(),
            };

            await chunkRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty, chunk);
        }
        
        
    }
}