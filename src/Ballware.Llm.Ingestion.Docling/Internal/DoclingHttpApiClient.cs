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
    public DoclingConversionDocument Document { get; set; }
}

public class DoclingHttpApiClient(DoclingOptions options) : IDoclingClient
{
    public async Task<string> ConvertToJsonAsync(string type, Stream input)
    {
        using var httpClient = new HttpClient();
        
        httpClient.BaseAddress = new Uri(options.Url);

        var request = new MultipartFormDataContent();

        var fileContent = new StreamContent(input);

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(type);

        request.Add(fileContent, "files", $"input{MimeTypes.MimeTypeMap.GetExtension(type)}");
        request.Add(new StringContent("json"), "output_format");

        var response = await httpClient.PostAsync("/v1/convert/file", request);

        response.EnsureSuccessStatusCode();

        var result = JsonConvert.DeserializeObject<DoclingConversionResponse>(await response.Content.ReadAsStringAsync());

        if (result?.Document.JsonContent == null)
        {
            throw new InvalidOperationException("Response does not contain json content");
        }
        
        return result.Document.JsonContent;
    }
    
    public async Task<string> ConvertToMarkdownAsync(string type, Stream input)
    {
        using var httpClient = new HttpClient();
        
        httpClient.BaseAddress = new Uri(options.Url);

        var request = new MultipartFormDataContent();

        var fileContent = new StreamContent(input);

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(type);

        request.Add(fileContent, "files", $"input{MimeTypes.MimeTypeMap.GetExtension(type)}");
        request.Add(new StringContent("markdown"), "output_format");
        request.Add(new StringContent("placeholder"), "image_export_mode");

        var response = await httpClient.PostAsync("/v1/convert/file", request);

        response.EnsureSuccessStatusCode();

        var responsePayload = await response.Content.ReadAsStringAsync();
        
        var result = JsonConvert.DeserializeObject<DoclingConversionResponse>(responsePayload);

        if (result?.Document.MarkdownContent == null)
        {
            throw new InvalidOperationException("Response does not contain markdown content");
        }
        
        return result.Document.MarkdownContent;
    }
}