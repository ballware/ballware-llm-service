using System.Security.Cryptography;
using System.Text;
using Ballware.Llm.Ingestion;
using Newtonsoft.Json.Linq;

namespace Ballware.Llm.Ingestion.Docling.Internal;

internal static class DoclingImageExtractor
{
    private static readonly string[] PayloadKeys =
    [
        "image_base64",
        "base64",
        "data",
        "binary",
        "blob",
        "content"
    ];

    private static readonly string[] ContentTypeKeys =
    [
        "content_type",
        "contentType",
        "mime_type",
        "mimetype",
        "mime"
    ];

    private static readonly string[] NameKeys =
    [
        "filename",
        "file_name",
        "name",
        "path",
        "uri",
        "source"
    ];

    private static readonly string[] DescriptionKeys =
    [
        "caption",
        "description",
        "text",
        "title",
        "alt"
    ];

    public static IReadOnlyList<IngestionImage> ExtractImages(string? jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return [];
        }

        JToken rootToken;

        try
        {
            rootToken = JToken.Parse(jsonContent);
        }
        catch
        {
            return [];
        }

        var result = new List<IngestionImage>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        if (rootToken is not JContainer rootContainer)
        {
            return [];
        }

        foreach (var objectToken in rootContainer.DescendantsAndSelf().OfType<JObject>())
        {
            if (!TryExtractImageFromObject(objectToken, out var image))
            {
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(image.Payload));
            if (!hashes.Add(hash))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(image.Name))
            {
                image.Name = $"image-{++index}{EvaluateFileExtension(image.ContentType)}";
            }

            result.Add(image);
        }

        return result;
    }

    private static bool TryExtractImageFromObject(JObject token, out IngestionImage image)
    {
        image = new IngestionImage();

        var payload = GetString(token, PayloadKeys);
        var contentType = GetString(token, ContentTypeKeys);

        if (TryParseDataUrl(payload, out var dataUrlType, out var dataUrlPayload))
        {
            payload = dataUrlPayload;
            contentType ??= dataUrlType;
        }

        if (!string.IsNullOrWhiteSpace(payload) && string.IsNullOrWhiteSpace(contentType))
        {
            var dataUrlInUri = GetString(token, ["uri", "source"]);
            if (TryParseDataUrl(dataUrlInUri, out var uriType, out _))
            {
                contentType = uriType;
            }
        }

        contentType ??= string.Empty;

        if (!contentType.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            var uriPayload = GetString(token, ["uri", "source"]);
            if (!TryParseDataUrl(uriPayload, out _, out payload))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var normalizedPayload = NormalizeBase64(payload);

        try
        {
            image.Payload = Convert.FromBase64String(normalizedPayload);
        }
        catch
        {
            return false;
        }

        if (image.Payload.Length == 0)
        {
            return false;
        }

        image.ContentType = contentType;
        image.Name = EvaluateImageName(GetString(token, NameKeys), contentType);
        image.Description = EvaluateDescription(token);
        image.Source = GetString(token, ["uri", "source", "path"]);

        return true;
    }

    private static string? EvaluateDescription(JObject token)
    {
        var directText = GetString(token, DescriptionKeys);
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        var captionsToken = token["captions"];
        if (captionsToken is not JArray captions)
        {
            return null;
        }

        var captionTexts = captions
            .Select(caption =>
            {
                if (caption is JValue value)
                {
                    return value.Value?.ToString();
                }

                if (caption is JObject obj)
                {
                    return GetString(obj, ["text", "caption", "description", "title"]);
                }

                return caption.ToString();
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToList();

        return captionTexts.Count > 0 ? string.Join(" ", captionTexts) : null;
    }

    private static string EvaluateImageName(string? candidate, string contentType)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var value = candidate;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            value = absoluteUri.LocalPath;
        }

        value = value.Replace('\\', '/');
        value = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Path.GetExtension(value).Length == 0)
        {
            value += EvaluateFileExtension(contentType);
        }

        return value;
    }

    private static string EvaluateFileExtension(string contentType)
    {
        var evaluated = MimeTypes.MimeTypeMap.GetExtension(contentType, false);
        if (string.IsNullOrWhiteSpace(evaluated))
        {
            return ".bin";
        }

        return evaluated.StartsWith('.') ? evaluated : $".{evaluated}";
    }

    private static string? GetString(JObject token, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!token.TryGetValue(key, StringComparison.InvariantCultureIgnoreCase, out var valueToken))
            {
                continue;
            }

            var value = valueToken.Type switch
            {
                JTokenType.String => valueToken.Value<string>(),
                JTokenType.Integer => valueToken.Value<long>().ToString(),
                JTokenType.Float => valueToken.Value<double>().ToString("G"),
                JTokenType.Boolean => valueToken.Value<bool>().ToString(),
                _ => valueToken.ToString()
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseDataUrl(string? value, out string? contentType, out string? payload)
    {
        contentType = null;
        payload = null;

        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("data:", StringComparison.InvariantCultureIgnoreCase))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(',');
        if (separatorIndex < 0)
        {
            return false;
        }

        var metadata = value[5..separatorIndex];
        var metadataParts = metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (metadataParts.Length == 0)
        {
            return false;
        }

        contentType = metadataParts[0];
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        payload = value[(separatorIndex + 1)..];
        return true;
    }

    private static string NormalizeBase64(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
