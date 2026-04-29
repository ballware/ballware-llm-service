using Ballware.Llm.Docling.Client;
using Ballware.Llm.Ingestion;
using Newtonsoft.Json;

namespace Ballware.Llm.Ingestion.Docling.Internal;

public class DoclingConversionDocument
{   
    [JsonProperty(PropertyName = "filename")]
    public required string Filename { get; set; }
    
    [JsonProperty(PropertyName = "md_content")]
    public string? MarkdownContent { get; set; }
    
    [JsonProperty(PropertyName = "json_content")]
    public string? JsonContent { get; set; }
    
    [JsonProperty(PropertyName = "html_content")]
    public string? HtmlContent { get; set; }
    
    [JsonProperty(PropertyName = "text_content")]
    public string? TextContent { get; set; }
    
    [JsonProperty(PropertyName = "status")]
    public required string Status { get; set; }
}

public class DoclingConversionResponse
{
    [JsonProperty(PropertyName = "document")]
    public required DoclingConversionDocument Document { get; set; }
}

public class DoclingHttpApiClient(DoclingApiClient apiClient) : IDoclingClient
{
    private const int PollingIntervalMs = 1000;

    public async Task<string> ConvertToJsonAsync(string type, Stream input)
    {
        var result = await ConvertAsync(type, input, "json");

        if (result.Document.JsonContent == null)
        {
            throw new InvalidOperationException("Response does not contain json content");
        }

        return result.Document.JsonContent;
    }

    public async Task<string> ConvertToMarkdownAsync(string type, Stream input)
    {
        var result = await ConvertAsync(type, input, "markdown", "placeholder");

        if (result.Document.MarkdownContent == null)
        {
            throw new InvalidOperationException("Response does not contain markdown content");
        }

        return result.Document.MarkdownContent;
    }

    public async Task<(string Markdown, IReadOnlyList<IngestionImage> Images)> ConvertToMarkdownWithImagesAsync(string type, Stream input)
    {
        var payload = await CopyToByteArrayAsync(input);

        using var markdownStream = new MemoryStream(payload, writable: false);
        var markdownResult = await ConvertAsync(type, markdownStream, "markdown", "placeholder");

        if (markdownResult.Document.MarkdownContent == null)
        {
            throw new InvalidOperationException("Response does not contain markdown content");
        }

        using var jsonStream = new MemoryStream(payload, writable: false);
        var jsonResult = await ConvertAsync(type, jsonStream, "json", "embedded");

        var images = DoclingImageExtractor.ExtractImages(jsonResult.Document.JsonContent);

        return (markdownResult.Document.MarkdownContent, images);
    }

    private async Task<DoclingConversionResponse> ConvertAsync(
        string type,
        Stream input,
        string outputFormat,
        string? imageExportMode = null)
    {
        var taskId = await apiClient.StartFileConversionAsync(
            input,
            type,
            MapOutputFormat(outputFormat),
            MapImageExportMode(imageExportMode));

        await WaitForCompletionAsync(taskId);

        return await GetResultAsync(taskId);
    }

    private async Task WaitForCompletionAsync(string taskId)
    {
        while (true)
        {
            var status = await apiClient.Task_status_poll_v1_status_poll__task_id__getAsync(taskId, null);

            switch (status?.Task_status)
            {
                case "success":
                    return;
                case "failure":
                    throw new InvalidOperationException($"Conversion failed for task {taskId}");
                default:
                    await Task.Delay(PollingIntervalMs);
                    break;
            }
        }
    }

    private async Task<DoclingConversionResponse> GetResultAsync(string taskId)
    {
        var result = JsonConvert.DeserializeObject<DoclingConversionResponse>(
            await apiClient.GetTaskResultJsonAsync(taskId));

        return result ?? throw new InvalidOperationException("No result in response");
    }

    private static OutputFormat MapOutputFormat(string outputFormat) => outputFormat switch
    {
        "json" => OutputFormat.Json,
        "markdown" => OutputFormat.Md,
        "html" => OutputFormat.Html,
        "text" => OutputFormat.Text,
        _ => throw new InvalidOperationException($"Unsupported output format: {outputFormat}")
    };

    private static ImageRefMode? MapImageExportMode(string? imageExportMode) => imageExportMode switch
    {
        null => null,
        "placeholder" => ImageRefMode.Placeholder,
        "embedded" => ImageRefMode.Embedded,
        "referenced" => ImageRefMode.Referenced,
        _ => throw new InvalidOperationException($"Unsupported image export mode: {imageExportMode}")
    };

    private static async Task<byte[]> CopyToByteArrayAsync(Stream input)
    {
        using var memoryStream = new MemoryStream();
        await input.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}
