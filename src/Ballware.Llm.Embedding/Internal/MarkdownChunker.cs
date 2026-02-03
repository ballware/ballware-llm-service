using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel.Text;
using Newtonsoft.Json.Linq;

namespace Ballware.Llm.Embedding.Internal;

internal sealed class MarkdownChunker : IChunker<string>
{
    [Experimental("SKEXP0050")]
    public IReadOnlyList<Chunk> Chunk(string markdown, ChunkRequest request)
    {
        var lines = TextChunker.SplitMarkDownLines(
            markdown,
            maxTokensPerLine: request.MaxTokensPerChunk);

        var parts = TextChunker.SplitMarkdownParagraphs(
            lines,
            maxTokensPerParagraph: request.MaxTokensPerChunk,
            overlapTokens: request.OverlapTokens);

        var chunks = new List<Chunk>(parts.Count);

        for (int i = 0; i < parts.Count; i++)
        {
            var text = parts[i].Trim();
            if (text.Length == 0) continue;

            var meta = new JObject
            {
                ["sourceType"] = "markdown",
                ["chunkIndex"] = i,
                ["originId"] = request.originId
            };

            chunks.Add(new Chunk(text, meta));
        }

        return chunks;
    }
}