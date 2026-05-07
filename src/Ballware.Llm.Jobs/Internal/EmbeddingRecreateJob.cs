using System.Collections.Immutable;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Embedding;
using Ballware.Llm.Embedding.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Ballware.Llm.Jobs.Internal;

public class EmbeddingRecreateJob(
    IServiceProvider serviceProvider,
    IOriginRepository originRepository,
    IEmbeddingRepository embeddingRepository,
    IChunkRepository chunkRepository,
    IImageRepository imageRepository,
    ChunkerRegistry chunkerRegistry,
    IEmbeddingEngine embeddingEngine,
    ChunkingOptions options) : IJob
{
    public static readonly JobKey Key = new JobKey("recreate", "embedding");

    public async Task Execute(IJobExecutionContext context)
    {
        var tenantId = context.MergedJobDataMap.GetGuidValue("tenantId");
        var userId = context.MergedJobDataMap.GetGuidValue("userId");
        var embeddingId = context.MergedJobDataMap.GetGuidValue("id");
        
        var embedding = await embeddingRepository.ByIdAsync(tenantId, "primary", ImmutableDictionary<string, object>.Empty, embeddingId);
        
        if (embedding == null)
        {
            throw new ArgumentNullException(nameof(embedding));
        }
        
        var origin = await originRepository.ByIdAsync(tenantId, "primary", ImmutableDictionary<string, object>.Empty, embedding.OriginId);
        
        if (origin == null)
        {
            throw new ArgumentNullException(nameof(origin));
        }
        
        var chunkerType = chunkerRegistry.GetChunker(origin.ContentType);

        if (chunkerType == null)
        {
            throw new ArgumentNullException(nameof(chunkerType));
        }
        
        await chunkRepository.RemoveByEmbeddingIdAsync(tenantId, embedding.Id);
        
        var chunker = (IChunker<string>)serviceProvider.GetRequiredService(chunkerType);

        var chunks = chunker.Chunk(origin.Content, new ChunkRequest(origin.Id, embedding.MaxTokensPerChunk ?? options.DefaultMaxTokensPerChunk, embedding.OverlapTokens ?? options.DefaultOverlapTokens));

        embedding.MaxTokensPerChunk ??= options.DefaultMaxTokensPerChunk;
        embedding.OverlapTokens ??= options.DefaultOverlapTokens;
        
        await embeddingRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty,
            embedding);
        
        foreach (var c in chunks)
        {
            var chunkEmbedding = await embeddingEngine.GetEmbeddingAsync(c.Text, embedding.Model);
            
            var chunk = new Data.Public.Chunk()
            {
                Id = Guid.NewGuid(),
                EmbeddingId = embedding.Id,
                Text = c.Text,
                Data = chunkEmbedding.ToArray(),
            };

            await chunkRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty, chunk);
        }

        var images = (await imageRepository.GetByOriginAsync(tenantId, origin.Id)).ToList();

        foreach (var image in images.Where(image => !string.IsNullOrWhiteSpace(image.Text)))
        {
            var chunkText = BuildImageChunkText(image);
            var imageChunkEmbedding = await embeddingEngine.GetEmbeddingAsync(chunkText, embedding.Model);

            var imageChunk = new Data.Public.Chunk()
            {
                Id = Guid.NewGuid(),
                EmbeddingId = embedding.Id,
                Text = chunkText,
                Data = imageChunkEmbedding.ToArray()
            };

            await chunkRepository.SaveAsync(tenantId, userId, "primary", ImmutableDictionary<string, object>.Empty, imageChunk);
        }
    }

    private static string BuildImageChunkText(Data.Public.Image image)
    {
        return $"[Bildinhalt] Name: {image.Name}{Environment.NewLine}Beschreibung: {image.Description}{Environment.NewLine}{image.Text}";
    }
}
