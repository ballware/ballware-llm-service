using System.Net.Http.Headers;
using Ballware.Llm.Ingestion.Docling.Configuration;

namespace Ballware.Llm.Ingestion.Docling.Internal;

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

        var result = await response.Content.ReadAsStringAsync();
        
        return result;
    }
}