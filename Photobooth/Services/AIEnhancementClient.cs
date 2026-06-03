using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Photobooth.Services
{
    public record AugmentationStyle(
        [property: JsonPropertyName("id")]          string  Id,
        [property: JsonPropertyName("name")]        string  Name,
        [property: JsonPropertyName("description")] string  Description,
        [property: JsonPropertyName("preview_url")] string? PreviewUrl
    );

    public class AIEnhancementClient
    {
        private readonly SettingsManager _settings;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

        private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        public AIEnhancementClient(SettingsManager settings) => _settings = settings;

        private string BaseUrl => _settings.AIServerUrl.TrimEnd('/');
        private string ApiKey  => _settings.AIApiKey;

        private void AddApiKey(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
                req.Headers.Add("X-Api-Key", ApiKey);
        }

        public async Task<List<AugmentationStyle>> GetStylesAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/external/styles");
            AddApiKey(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<AugmentationStyle>>(body, _json)
                   ?? throw new InvalidOperationException("Empty styles response");
        }

        public async Task<List<string>> AugmentImagesAsync(List<string> photoPaths, string styleId, string outputDir)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(styleId), "style_id");

            foreach (var path in photoPaths)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                form.Add(content, "images", Path.GetFileName(path));
            }

            var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/external/augment") { Content = form };
            AddApiKey(req);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ExternalAugmentResponse>(body, _json)
                         ?? throw new InvalidOperationException("Empty augmentation response");

            Directory.CreateDirectory(outputDir);

            var safeStyle  = string.Concat(styleId.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
            var savedPaths = new List<string>();
            for (int i = 0; i < result.Images.Count; i++)
            {
                var stem     = Path.GetFileNameWithoutExtension(photoPaths[i]);
                var imgBytes = Convert.FromBase64String(result.Images[i].AugmentedB64);
                var outPath  = Path.Combine(outputDir, $"{stem}_{safeStyle}_enhanced.png");
                await File.WriteAllBytesAsync(outPath, imgBytes);
                savedPaths.Add(outPath);
                Log.Debug("AI-enhanced image {I} ({Style}) saved to {Path}", i + 1, styleId, outPath);
            }

            return savedPaths;
        }
    }

    file record ExternalAugmentResponse(
        [property: JsonPropertyName("images")] List<AugmentedImageDto> Images
    );

    file record AugmentedImageDto(
        [property: JsonPropertyName("augmented_b64")] string AugmentedB64
    );
}
