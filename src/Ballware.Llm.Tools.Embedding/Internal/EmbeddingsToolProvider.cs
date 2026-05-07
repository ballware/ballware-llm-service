using System.Globalization;
using System.Text;
using System.Text.Json;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Embedding;

namespace Ballware.Llm.Tools.Embedding.Internal;

internal class EmbeddingsToolProvider(
    IOriginRepository originRepository,
    IEmbeddingRepository embeddingRepository,
    IChunkRepository chunkRepository,
    IImageRepository imageRepository,
    IEmbeddingEngine embeddingEngine) : IToolProvider
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 20;
    private const int DefaultMaxContentChars = 6000;
    private const int MaxContentChars = 20000;

    public string Namespace { get; } = "embeddings";

    public IEnumerable<Tool> GetToolsForUser(Guid tenantId, Guid userId, IDictionary<string, object> claims)
    {
        return
        [
            new Tool(
                "origins.list",
                "List global knowledge origins available to the current tenant, including how many active embeddings each origin provides.",
                [],
                async _ => await ListOriginsAsync(tenantId)),
            new Tool(
                "origins.content",
                "Read additional knowledge content from a specific origin (document/information source) used to enrich LLM context.",
                [
                    new ToolParameter("originId", "string", "The origin GUID to read from."),
                    new ToolParameter("searchTerm", "string", "Optional keyword to return focused snippets instead of full content."),
                    new ToolParameter("maxChars", "integer", "Optional maximum number of characters returned from the origin content.")
                ],
                async args => await GetOriginContentAsync(tenantId, claims, args)),
            new Tool(
                "origins.query",
                "Query the tenant knowledge base semantically across active embeddings and return the most relevant evidence chunks.",
                [
                    new ToolParameter("query", "string", "Natural-language question or semantic search query."),
                    new ToolParameter("originId", "string", "Optional origin GUID to restrict search to one knowledge source."),
                    new ToolParameter("topK", "integer", "Optional number of top relevant chunks to return per embedding.")
                ],
                async args => await QueryEmbeddingsAsync(tenantId, claims, args))
        ];
    }

    private async Task<string> ListOriginsAsync(Guid tenantId)
    {
        var origins = (await originRepository.GetQueryableOriginsAsync(tenantId)).ToList();

        if (!origins.Any())
        {
            return "Keine globalen Origins vorhanden.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Globale Origins:");

        foreach (var origin in origins)
        {
            var activeEmbeddingList = (await embeddingRepository.GetActiveEmbeddingsForOrigin(tenantId, origin.Id)).ToList();
            var imageCount = (await imageRepository.GetByOriginAsync(tenantId, origin.Id)).Count();

            builder.AppendLine(
                $"- Id: {origin.Id} | Name: {origin.Name} | Embeddings: {activeEmbeddingList.Count} | Images: {imageCount} | ContentType: {origin.ContentType}");
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string> GetOriginContentAsync(Guid tenantId, IDictionary<string, object> claims, IDictionary<string, object?> args)
    {
        var originIdText = AsString(args, "originId");
        if (!Guid.TryParse(originIdText, out var originId))
        {
            return "Ungültige oder fehlende originId.";
        }

        var origin = await originRepository.ByIdAsync(tenantId, "primary", claims, originId);
        if (origin == null)
        {
            return "Origin wurde nicht gefunden.";
        }

        var maxChars = Clamp(AsInt(args, "maxChars") ?? DefaultMaxContentChars, 1000, MaxContentChars);
        var searchTerm = AsString(args, "searchTerm");
        var images = (await imageRepository.GetByOriginAsync(tenantId, origin.Id)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"Origin: {origin.Name}");
        builder.AppendLine($"OriginId: {origin.Id}");
        builder.AppendLine($"ContentType: {origin.ContentType}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var snippets = BuildSnippets(origin.Content ?? string.Empty, searchTerm, 6, 180);

            if (snippets.Count == 0)
            {
                builder.AppendLine($"Keine Treffer für '{searchTerm}'.");
            }
            else
            {
                builder.AppendLine($"Treffer für '{searchTerm}':");
                foreach (var snippet in snippets)
                {
                    builder.AppendLine($"- {snippet}");
                }
            }

            var imageSnippets = images
                .Where(image =>
                    image.Text.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase) ||
                    image.Description.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase) ||
                    image.Name.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase))
                .Take(6)
                .ToList();

            if (imageSnippets.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"Bildtreffer für '{searchTerm}':");
                foreach (var image in imageSnippets)
                {
                    builder.AppendLine($"- ImageId: {image.Id} | Name: {image.Name} | {TrimToMax(image.Text, 220).Replace(Environment.NewLine, " ")}");
                }
            }
        }
        else
        {
            builder.AppendLine(TrimToMax(origin.Content ?? string.Empty, maxChars));

            if (images.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Extrahierte Bilder:");

                foreach (var image in images.Take(20))
                {
                    builder.AppendLine($"- ImageId: {image.Id} | Name: {image.Name} | ContentType: {image.ContentType} | Beschreibung: {TrimToMax(image.Description, 180)}");
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string> QueryEmbeddingsAsync(Guid tenantId, IDictionary<string, object> claims, IDictionary<string, object?> args)
    {
        var query = AsString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Parameter 'query' ist erforderlich.";
        }

        var topK = Clamp(AsInt(args, "topK") ?? DefaultTopK, 1, MaxTopK);
        var originIdText = AsString(args, "originId");
        var origins = new List<Data.Public.Origin>();

        if (!string.IsNullOrWhiteSpace(originIdText))
        {
            if (!Guid.TryParse(originIdText, out var originId))
            {
                return "Ungültige originId.";
            }

            var origin = await originRepository.ByIdAsync(tenantId, "primary", claims, originId);
            if (origin == null)
            {
                return "Origin wurde nicht gefunden.";
            }

            origins.Add(origin);
        }
        else
        {
            origins.AddRange(await originRepository.GetGlobalOriginsAsync(tenantId));
        }

        if (!origins.Any())
        {
            return "Keine Origins für die Abfrage verfügbar.";
        }

        var vectorsByModel = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        var results = new List<(Data.Public.Origin Origin, Data.Public.Embedding Embedding, List<string> Chunks)>();

        foreach (var origin in origins)
        {
            var embeddings = (await embeddingRepository.GetActiveEmbeddingsForOrigin(tenantId, origin.Id)).ToList();

            foreach (var embedding in embeddings)
            {
                var modelKey = embedding.Model ?? string.Empty;
                if (!vectorsByModel.TryGetValue(modelKey, out var vector))
                {
                    vector = await embeddingEngine.GetEmbeddingAsync(query, embedding.Model);
                    vectorsByModel[modelKey] = vector;
                }

                var chunkList = (await chunkRepository.FetchTopKSimilarChunksAsync(tenantId, embedding.Id, vector, topK))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                if (chunkList.Count > 0)
                {
                    results.Add((origin, embedding, chunkList));
                }
            }
        }

        if (results.Count == 0)
        {
            return "Keine passenden Inhalte in aktiven Embeddings gefunden.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Semantische Treffer für: {query}");

        foreach (var result in results)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"Source: {result.Origin.Name} | OriginId: {result.Origin.Id} | EmbeddingId: {result.Embedding.Id} | Model: {result.Embedding.Model}");

            foreach (var chunk in result.Chunks)
            {
                builder.AppendLine($"- {chunk}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? AsString(IDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => jsonElement.GetRawText()
            };
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int? AsInt(IDictionary<string, object?> args, string key)
    {
        var value = AsString(args, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static string TrimToMax(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + Environment.NewLine + "... [gekürzt]";
    }

    private static List<string> BuildSnippets(string content, string searchTerm, int maxSnippets, int contextChars)
    {
        var snippets = new List<string>();
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(searchTerm))
        {
            return snippets;
        }

        var startIndex = 0;
        while (snippets.Count < maxSnippets)
        {
            var hitIndex = content.IndexOf(searchTerm, startIndex, StringComparison.InvariantCultureIgnoreCase);
            if (hitIndex < 0)
            {
                break;
            }

            var from = Math.Max(0, hitIndex - contextChars);
            var to = Math.Min(content.Length, hitIndex + searchTerm.Length + contextChars);
            var snippet = content[from..to].Replace('\n', ' ').Replace('\r', ' ').Trim();

            snippets.Add(snippet);
            startIndex = hitIndex + searchTerm.Length;
        }

        return snippets;
    }
}
