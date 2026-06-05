using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Services;
using Xunit;

namespace Photobooth.Tests.Integration;

public sealed class AIEnhancementClientTests : IDisposable
{
    private readonly string _tempSettings;
    private readonly SettingsManager _settings;

    public AIEnhancementClientTests()
    {
        _tempSettings = Path.GetTempFileName();
        File.Delete(_tempSettings);
        _settings = new SettingsManager(_tempSettings);
        // Point at a deterministic base URL for assertions
        _settings.SetAIServerUrl("http://test-server:9000");
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettings))
            File.Delete(_tempSettings);
    }

    // ── StubHandler ────────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "[]";
        public HttpRequestMessage? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    // ── GetStylesAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStylesAsync_ReturnsStyles_WhenServerRespondsWithValidJson()
    {
        var stub = new StubHandler
        {
            ResponseBody = """
                [
                    {"id":"pop","name":"Pop Art","description":"Bold colours","preview_url":null},
                    {"id":"bw","name":"Black & White","description":"Monochrome","preview_url":"http://x/bw.png"}
                ]
                """
        };
        var client = new AIEnhancementClient(_settings, new HttpClient(stub));

        var styles = await client.GetStylesAsync();

        Assert.Equal(2, styles.Count);
        Assert.Equal("pop", styles[0].Id);
        Assert.Equal("Pop Art", styles[0].Name);
        Assert.Equal("bw", styles[1].Id);
        Assert.Equal("http://x/bw.png", styles[1].PreviewUrl);
    }

    [Fact]
    public async Task GetStylesAsync_SendsRequestToCorrectUrl()
    {
        var stub = new StubHandler { ResponseBody = "[]" };
        var client = new AIEnhancementClient(_settings, new HttpClient(stub));

        await client.GetStylesAsync();

        Assert.NotNull(stub.LastRequest);
        Assert.Equal(HttpMethod.Get, stub.LastRequest!.Method);
        Assert.Equal("http://test-server:9000/api/external/styles", stub.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetStylesAsync_ThrowsHttpRequestException_OnHttpError()
    {
        var stub = new StubHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "Internal Server Error"
        };
        var client = new AIEnhancementClient(_settings, new HttpClient(stub));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStylesAsync());
    }

    [Fact]
    public async Task GetStylesAsync_IncludesApiKey_WhenApiKeyIsConfigured()
    {
        _settings.SetAIApiKey("secret-key-123");
        var stub = new StubHandler { ResponseBody = "[]" };
        var client = new AIEnhancementClient(_settings, new HttpClient(stub));

        await client.GetStylesAsync();

        Assert.NotNull(stub.LastRequest);
        Assert.True(stub.LastRequest!.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Contains("secret-key-123", values);
    }

    [Fact]
    public async Task GetStylesAsync_OmitsApiKeyHeader_WhenApiKeyIsEmpty()
    {
        _settings.SetAIApiKey("");
        var stub = new StubHandler { ResponseBody = "[]" };
        var client = new AIEnhancementClient(_settings, new HttpClient(stub));

        await client.GetStylesAsync();

        Assert.NotNull(stub.LastRequest);
        Assert.False(stub.LastRequest!.Headers.Contains("X-Api-Key"));
    }

    // ── AugmentImagesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AugmentImagesAsync_SendsMultipartRequestToCorrectUrl()
    {
        // Create a real temp image file so File.ReadAllBytesAsync succeeds
        var tempImage = Path.GetTempFileName();
        var tempOutput = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await File.WriteAllBytesAsync(tempImage, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // minimal JPEG header

            var stub = new StubHandler
            {
                ResponseBody = """{"images":[{"augmented_b64":"AAEC"}]}"""
            };
            var client = new AIEnhancementClient(_settings, new HttpClient(stub));

            var results = await client.AugmentImagesAsync(new() { tempImage }, "pop", tempOutput);

            Assert.Single(results);
            Assert.NotNull(stub.LastRequest);
            Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
            Assert.Equal("http://test-server:9000/api/external/augment", stub.LastRequest.RequestUri!.ToString());
        }
        finally
        {
            if (File.Exists(tempImage)) File.Delete(tempImage);
            if (Directory.Exists(tempOutput)) Directory.Delete(tempOutput, recursive: true);
        }
    }

    [Fact]
    public async Task AugmentImagesAsync_SavesDecodedImageToOutputDir()
    {
        var tempImage = Path.GetTempFileName();
        var tempOutput = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await File.WriteAllBytesAsync(tempImage, new byte[] { 1, 2, 3 });

            // base64("hello") = "aGVsbG8="
            var stub = new StubHandler
            {
                ResponseBody = """{"images":[{"augmented_b64":"aGVsbG8="}]}"""
            };
            var client = new AIEnhancementClient(_settings, new HttpClient(stub));

            var results = await client.AugmentImagesAsync(new() { tempImage }, "bw", tempOutput);

            Assert.Single(results);
            Assert.True(File.Exists(results[0]));
            var written = await File.ReadAllBytesAsync(results[0]);
            Assert.Equal(Encoding.ASCII.GetBytes("hello"), written);
        }
        finally
        {
            if (File.Exists(tempImage)) File.Delete(tempImage);
            if (Directory.Exists(tempOutput)) Directory.Delete(tempOutput, recursive: true);
        }
    }

    [Fact]
    public async Task AugmentImagesAsync_ThrowsHttpRequestException_OnHttpError()
    {
        var tempImage = Path.GetTempFileName();
        var tempOutput = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await File.WriteAllBytesAsync(tempImage, new byte[] { 1, 2, 3 });

            var stub = new StubHandler
            {
                StatusCode = HttpStatusCode.BadRequest,
                ResponseBody = "Bad Request"
            };
            var client = new AIEnhancementClient(_settings, new HttpClient(stub));

            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.AugmentImagesAsync(new() { tempImage }, "pop", tempOutput));
        }
        finally
        {
            if (File.Exists(tempImage)) File.Delete(tempImage);
            if (Directory.Exists(tempOutput)) Directory.Delete(tempOutput, recursive: true);
        }
    }

    [Fact]
    public async Task AugmentImagesAsync_StyleIdSanitizedInOutputFileName()
    {
        var tempImage = Path.GetTempFileName();
        var stem = Path.GetFileNameWithoutExtension(tempImage);
        var tempOutput = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await File.WriteAllBytesAsync(tempImage, new byte[] { 1, 2, 3 });

            var stub = new StubHandler
            {
                ResponseBody = """{"images":[{"augmented_b64":"AAEC"}]}"""
            };
            var client = new AIEnhancementClient(_settings, new HttpClient(stub));

            // Style ID with special characters that should be sanitized to underscores
            var results = await client.AugmentImagesAsync(new() { tempImage }, "my style/v2", tempOutput);

            Assert.Single(results);
            // Sanitized: spaces and / become _
            Assert.Contains("my_style_v2", Path.GetFileName(results[0]));
        }
        finally
        {
            if (File.Exists(tempImage)) File.Delete(tempImage);
            if (Directory.Exists(tempOutput)) Directory.Delete(tempOutput, recursive: true);
        }
    }
}
