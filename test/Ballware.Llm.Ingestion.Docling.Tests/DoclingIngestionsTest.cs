using System.Text;
using System.Text.Json;
using Ballware.Llm.Docling.Client;
using Ballware.Llm.Ingestion.Docling.Internal;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Uri = System.Uri;

namespace Ballware.Llm.Ingestion.Docling.Tests;

[TestFixture]
[NonParallelizable]
public class DoclingIngestionsTest
{
    [Test]
    public async Task MarkdownIngestor_ShouldReturnMarkdownAndExtractedImages()
    {
        await using var container = await DoclingMockContainer.StartAsync();

        await container.AddStartConversionMappingAsync("md", "placeholder", "task-markdown");
        await container.AddTaskStatusMappingAsync("task-markdown", "success");
        await container.AddTaskResultMappingAsync(
            "task-markdown",
            markdownContent: await ReadTestDataAsync("Responses/markdown-output.md"));

        await container.AddStartConversionMappingAsync("json", "embedded", "task-json-with-images");
        await container.AddTaskStatusMappingAsync("task-json-with-images", "success");
        await container.AddTaskResultMappingAsync(
            "task-json-with-images",
            jsonContent: await ReadTestDataAsync("Responses/json-with-images.json"));

        using var httpClient = CreateHttpClient(container.BaseAddress);
        var ingestor = new DoclingMarkdownIngestor(new DoclingHttpApiClient(new DoclingApiClient(httpClient)));

        await using var payload = File.OpenRead(GetTestDataPath("Inputs/sample-article.md"));

        var result = await ingestor.IngestFromStreamAsync("text/markdown", payload);

        Assert.Multiple(() =>
        {
            Assert.That(result.Format, Is.EqualTo("text/markdown"));
            Assert.That(result.Payload, Does.Contain("# Umsatzbericht Q1"));
            Assert.That(result.Payload, Does.Contain("| Monat | Umsatz |"));
            Assert.That(result.Images.Count, Is.EqualTo(2), "Duplicate image payloads should be removed.");
        });

        var pngImage = result.Images.Single(image => image.ContentType == "image/png");
        var jpegImage = result.Images.Single(image => image.ContentType == "image/jpeg");

        Assert.Multiple(() =>
        {
            Assert.That(pngImage.Name, Is.EqualTo("chart.png"));
            Assert.That(pngImage.Description, Is.EqualTo("Revenue chart"));
            Assert.That(pngImage.Payload, Is.EqualTo(Convert.FromBase64String("aW1hZ2UtYnl0ZXMtMQ==")));
            Assert.That(jpegImage.Name, Does.StartWith("image-1."));
            Assert.That(jpegImage.Description, Is.EqualTo("Product overview"));
            Assert.That(jpegImage.Payload, Is.EqualTo(Convert.FromBase64String("aW1hZ2UtYnl0ZXMtMg==")));
        });
    }

    [Test]
    public async Task JsonIngestor_ShouldReturnStructuredJsonPayload()
    {
        await using var container = await DoclingMockContainer.StartAsync();

        await container.AddStartConversionMappingAsync("json", null, "task-json");
        await container.AddTaskStatusMappingAsync("task-json", "success");
        await container.AddTaskResultMappingAsync(
            "task-json",
            jsonContent: await ReadTestDataAsync("Responses/json-structured-output.json"));

        using var httpClient = CreateHttpClient(container.BaseAddress);
        var ingestor = new DoclingJsonIngestor(new DoclingHttpApiClient(new DoclingApiClient(httpClient)));

        await using var payload = File.OpenRead(GetTestDataPath("Inputs/sample-structure.csv"));

        var result = await ingestor.IngestFromStreamAsync("text/csv", payload);

        Assert.That(result.Format, Is.EqualTo("application/json"));
        Assert.That(result.Images, Is.Empty);

        using var json = JsonDocument.Parse(result.Payload);
        var document = json.RootElement.GetProperty("document");

        Assert.Multiple(() =>
        {
            Assert.That(document.GetProperty("title").GetString(), Is.EqualTo("Artikel mit Struktur"));
            Assert.That(
                document.GetProperty("metadata").GetProperty("language").GetString(),
                Is.EqualTo("de"));
            Assert.That(
                document.GetProperty("metadata").GetProperty("keywords").GetArrayLength(),
                Is.EqualTo(3));
        });
    }

    [Test]
    public async Task JsonIngestor_ShouldThrowIfDoclingReportsFailure()
    {
        await using var container = await DoclingMockContainer.StartAsync();

        await container.AddStartConversionMappingAsync("json", null, "task-failure");
        await container.AddTaskStatusMappingAsync("task-failure", "failure");

        using var httpClient = CreateHttpClient(container.BaseAddress);
        var ingestor = new DoclingJsonIngestor(new DoclingHttpApiClient(new DoclingApiClient(httpClient)));

        await using var payload = File.OpenRead(GetTestDataPath("Inputs/sample-image.svg"));

        AsyncTestDelegate action = async () => { await ingestor.IngestFromStreamAsync("image/svg+xml", payload); };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.That(exception?.Message, Does.Contain("Conversion failed for task task-failure"));
    }

    private static HttpClient CreateHttpClient(Uri baseAddress) =>
        new()
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

    private static async Task<string> ReadTestDataAsync(string relativePath) =>
        await File.ReadAllTextAsync(GetTestDataPath(relativePath));

    private static string GetTestDataPath(string relativePath)
    {
        var normalizedPath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(AppContext.BaseDirectory, "TestData", normalizedPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Missing test data file: {fullPath}");
        }

        return fullPath;
    }

    private sealed class DoclingMockContainer : IAsyncDisposable
    {
        private const ushort WireMockPort = 8080;
        private readonly IContainer _container;
        private readonly HttpClient _adminClient;

        private DoclingMockContainer(IContainer container, HttpClient adminClient, Uri baseAddress)
        {
            _container = container;
            _adminClient = adminClient;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public static async Task<DoclingMockContainer> StartAsync()
        {
            var container = new ContainerBuilder()
                .WithImage("wiremock/wiremock:3.13.1")
                .WithPortBinding(WireMockPort, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(WireMockPort))
                .Build();

            try
            {
                await container.StartAsync();
            }
            catch (Exception exception)
            {
                await container.DisposeAsync();
                Assert.Ignore($"Docker/WireMock konnte nicht gestartet werden: {exception.Message}");
                throw;
            }

            var baseAddress = new Uri($"http://127.0.0.1:{container.GetMappedPublicPort(WireMockPort)}/");
            var adminClient = new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };

            return new DoclingMockContainer(container, adminClient, baseAddress);
        }

        public async Task AddStartConversionMappingAsync(string outputFormat, string? imageExportMode, string taskId)
        {
            var bodyPatterns = new List<object>
            {
                new { contains = "name=output_format" },
                new { contains = outputFormat }
            };

            if (string.IsNullOrWhiteSpace(imageExportMode))
            {
                bodyPatterns.Add(new { doesNotContain = "name=image_export_mode" });
            }
            else
            {
                bodyPatterns.Add(new { contains = "name=image_export_mode" });
                bodyPatterns.Add(new { contains = imageExportMode });
            }

            await AddMappingAsync(new
            {
                request = new
                {
                    method = "POST",
                    urlPath = "/v1/convert/file/async",
                    bodyPatterns
                },
                response = new
                {
                    status = 200,
                    headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    jsonBody = new
                    {
                        task_id = taskId,
                        task_type = "convert",
                        task_status = "pending"
                    }
                }
            });
        }

        public async Task AddTaskStatusMappingAsync(string taskId, string status)
        {
            await AddMappingAsync(new
            {
                request = new
                {
                    method = "GET",
                    urlPath = $"/v1/status/poll/{taskId}"
                },
                response = new
                {
                    status = 200,
                    headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    jsonBody = new
                    {
                        task_id = taskId,
                        task_type = "convert",
                        task_status = status
                    }
                }
            });
        }

        public async Task AddTaskResultMappingAsync(string taskId, string? markdownContent = null, string? jsonContent = null)
        {
            await AddMappingAsync(new
            {
                request = new
                {
                    method = "GET",
                    urlPath = $"/v1/result/{taskId}"
                },
                response = new
                {
                    status = 200,
                    headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                    jsonBody = new
                    {
                        document = new
                        {
                            filename = "sample",
                            md_content = markdownContent,
                            json_content = jsonContent,
                            status = "success"
                        }
                    }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _adminClient.Dispose();
            await _container.DisposeAsync();
        }

        private async Task AddMappingAsync(object mapping)
        {
            using var mappingContent = new StringContent(
                JsonSerializer.Serialize(mapping),
                Encoding.UTF8,
                "application/json");

            using var response = await _adminClient.PostAsync("__admin/mappings", mappingContent);
            response.EnsureSuccessStatusCode();
        }
    }
}
