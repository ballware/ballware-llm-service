using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace Ballware.Llm.Docling.Client;

public partial class DoclingApiClient
{
    public async Task<string> StartFileConversionAsync(
        Stream input,
        string contentType,
        OutputFormat outputFormat,
        ImageRefMode? imageExportMode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        using var request = new HttpRequestMessage();
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(input);

        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "files", "input");
        content.Add(
            new StringContent(ConvertToString(outputFormat, CultureInfo.InvariantCulture)),
            "output_format");

        if (imageExportMode != null)
        {
            content.Add(
                new StringContent(ConvertToString(imageExportMode, CultureInfo.InvariantCulture)),
                "image_export_mode");
        }

        request.Content = content;
        request.Method = HttpMethod.Post;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        var urlBuilder = new StringBuilder();
        urlBuilder.Append("v1/convert/file/async");

        PrepareRequest(_httpClient, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new System.Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(_httpClient, request, url);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var headers = CollectHeaders(response);

        ProcessResponse(_httpClient, response);

        var statusCode = (int)response.StatusCode;
        if (statusCode == 200)
        {
            var objectResponse =
                await ReadObjectResponseAsync<TaskStatusResponse>(response, headers, cancellationToken)
                    .ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new SwaggerException(
                    "Response was null which was not expected.",
                    statusCode,
                    objectResponse.Text,
                    headers,
                    null);
            }

            return objectResponse.Object.Task_id;
        }

        if (statusCode == 422)
        {
            var objectResponse =
                await ReadObjectResponseAsync<HTTPValidationError>(response, headers, cancellationToken)
                    .ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new SwaggerException(
                    "Response was null which was not expected.",
                    statusCode,
                    objectResponse.Text,
                    headers,
                    null);
            }

            throw new SwaggerException<HTTPValidationError>(
                "Validation Error",
                statusCode,
                objectResponse.Text,
                headers,
                objectResponse.Object,
                null);
        }

        var responseData = response.Content == null
            ? null
            : await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        throw new SwaggerException(
            $"The HTTP status code of the response was not expected ({statusCode}).",
            statusCode,
            responseData,
            headers,
            null);
    }

    public async Task<string> GetTaskResultJsonAsync(string taskId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Get;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        var urlBuilder = new StringBuilder();
        urlBuilder.Append("v1/result/");
        urlBuilder.Append(System.Uri.EscapeDataString(ConvertToString(taskId, CultureInfo.InvariantCulture)));

        PrepareRequest(_httpClient, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new System.Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(_httpClient, request, url);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var headers = CollectHeaders(response);

        ProcessResponse(_httpClient, response);

        var statusCode = (int)response.StatusCode;
        if (statusCode == 200)
        {
            return await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        }

        if (statusCode == 422)
        {
            var objectResponse =
                await ReadObjectResponseAsync<HTTPValidationError>(response, headers, cancellationToken)
                    .ConfigureAwait(false);
            if (objectResponse.Object == null)
            {
                throw new SwaggerException(
                    "Response was null which was not expected.",
                    statusCode,
                    objectResponse.Text,
                    headers,
                    null);
            }

            throw new SwaggerException<HTTPValidationError>(
                "Validation Error",
                statusCode,
                objectResponse.Text,
                headers,
                objectResponse.Object,
                null);
        }

        var responseData = response.Content == null
            ? null
            : await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        throw new SwaggerException(
            $"The HTTP status code of the response was not expected ({statusCode}).",
            statusCode,
            responseData,
            headers,
            null);
    }

    private static Dictionary<string, IEnumerable<string>> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();
        foreach (var item in response.Headers)
        {
            headers[item.Key] = item.Value;
        }

        if (response.Content == null || response.Content.Headers == null)
        {
            return headers;
        }

        foreach (var item in response.Content.Headers)
        {
            headers[item.Key] = item.Value;
        }

        return headers;
    }
}
