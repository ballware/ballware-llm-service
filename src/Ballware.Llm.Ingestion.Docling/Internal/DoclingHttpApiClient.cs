using System.Net.Http.Headers;
using Ballware.Llm.Ingestion.Docling.Configuration;
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

public class DoclingTaskResponse
{
    [JsonProperty(PropertyName = "task_id")]
    public required string TaskId { get; set; }
}

public class DoclingStatusResponse
{
    [JsonProperty(PropertyName = "task_id")]
    public required string TaskId { get; set; }

    [JsonProperty(PropertyName = "status")]
    public required string Status { get; set; }
}

public class DoclingHttpApiClient(DoclingOptions options) : IDoclingClient
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

    private async Task<DoclingConversionResponse> ConvertAsync(
        string type,
        Stream input,
        string outputFormat,
        string? imageExportMode = null)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        httpClient.BaseAddress = new Uri(options.Url);

        var taskId = await StartConversionAsync(httpClient, type, input, outputFormat, imageExportMode);

        await WaitForCompletionAsync(httpClient, taskId);

        return await GetResultAsync(httpClient, taskId);
    }

    private async Task<string> StartConversionAsync(
        HttpClient httpClient,
        string type,
        Stream input,
        string outputFormat,
        string? imageExportMode)
    {
        var request = new MultipartFormDataContent();
        var fileContent = new StreamContent(input);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(type);

        request.Add(fileContent, "files", $"input{MimeTypes.MimeTypeMap.GetExtension(type)}");
        request.Add(new StringContent(outputFormat), "output_format");

        if (imageExportMode != null)
        {
            request.Add(new StringContent(imageExportMode), "image_export_mode");
        }

        var response = await httpClient.PostAsync("/v1/convert/file/async", request);
        response.EnsureSuccessStatusCode();

        var result = JsonConvert.DeserializeObject<DoclingTaskResponse>(
            await response.Content.ReadAsStringAsync());

        return result?.TaskId ?? throw new InvalidOperationException("No task_id in response");
    }

    private async Task WaitForCompletionAsync(HttpClient httpClient, string taskId)
    {
        while (true)
        {
            var response = await httpClient.GetAsync($"/v1/status/poll/{taskId}");
            response.EnsureSuccessStatusCode();

            var status = JsonConvert.DeserializeObject<DoclingStatusResponse>(
                await response.Content.ReadAsStringAsync());

            switch (status?.Status)
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

    private async Task<DoclingConversionResponse> GetResultAsync(HttpClient httpClient, string taskId)
    {
        var response = await httpClient.GetAsync($"/v1/result/{taskId}");
        response.EnsureSuccessStatusCode();

        var result = JsonConvert.DeserializeObject<DoclingConversionResponse>(
            await response.Content.ReadAsStringAsync());

        return result ?? throw new InvalidOperationException("No result in response");
    }
}