// Namespace dan library standar untuk autentikasi, JSON, dan dependensi.
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GeminiAIServices.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

[ApiController]
[Route("api/[controller]")]
public class GeminiController : ControllerBase
{
    // Konstanta API Key dan URL endpoint Gemini, hanya di-set sekali.
    private static readonly string _apiKey = Constan.CONST_GOOGLE_API_KEY;
    private static readonly string _endpoint =
        Constan.CONST_URL_GOOGLE_API_GEMINI_FLASH_20_TEXT + _apiKey;

    // Opsi konfigurasi JSON serializer (mengabaikan null, camelCase).
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Dependency dari service
    private readonly JWTHelper _jwtHelper;
    private readonly JWTOptions _jwtOptions;
    private readonly HttpClient _client;
    private readonly IMemoryCache _cache;

    // Constructor untuk inject dependency
    public GeminiController(
        JWTHelper jwtHelper,
        IOptions<JWTOptions> jwtOptions,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache
    )
    {
        _jwtHelper = jwtHelper;
        _jwtOptions = jwtOptions.Value;
        _client = httpClientFactory.CreateClient("GeminiClient");
        _cache = memoryCache;
    }

    /// <summary>
    /// Endpoint untuk generate konten berbasis teks dari prompt.
    /// </summary>
    [HttpPost("text-only")]
    [Authorize]
    public async Task<IActionResult> GenerateText([FromBody] GeminiRequest request)
    {
        // Validasi request kosong atau prompt kosong
        if (request == null || string.IsNullOrWhiteSpace(request.prompt))
            return BadRequest(new { data = (object?)null });

        // Ambil userId dari JWT
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { data = (object?)null });

        // Gabungkan prompt dengan instruksi personalisasi, lalu generate key untuk cache
        var promptText = Constan.STR_PERSONAL_1_MODEL_GEMINI + request.prompt;
        var cacheKey = $"gemini:{promptText.GetHashCode()}";

        // Cek apakah hasilnya sudah ada di cache
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml))
            return Ok(new { html = cachedHtml });

        // Persiapkan payload request ke Gemini API
        var body = JsonSerializer.Serialize(
            new
            {
                contents = new[] { new { parts = new[] { new { text = promptText } } } },
                generationConfig = new
                {
                    temperature = 0.95,
                    topP = 0.9,
                    topK = 40,
                    maxOutputTokens = 2048,
                    responseMimeType = "application/json",
                },
            },
            _jsonOptions
        );

        // Kirim request POST ke Gemini
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(_endpoint, content).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return StatusCode((int)response.StatusCode, new { error = err });
        }

        // Baca dan parsing response JSON Gemini
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var json = await JsonSerializer.DeserializeAsync<JsonNode>(stream).ConfigureAwait(false);
        var rawJson = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

        if (string.IsNullOrWhiteSpace(rawJson))
            return StatusCode(502, new { error = "Gemini returned empty response." });

        // Parse hasil HTML dari isi teks
        var finalJson = JsonNode.Parse(rawJson);
        var html = finalJson?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "");

        // Ambil informasi token dari usageMetadata
        var usage = json?["usageMetadata"];
        int inputToken = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int outputToken = usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        int totalToken = usage?["totalTokenCount"]?.GetValue<int>() ?? 0;

        // Simpan ke cache untuk 5 menit
        _cache.Set(cacheKey, html, TimeSpan.FromMinutes(5));

        return Ok(
            new
            {
                html,
                input_token = inputToken,
                output_token = outputToken,
                total_token = totalToken,
            }
        );
    }

    /// <summary>
    /// Endpoint untuk generate konten dari teks dan gambar (image inline).
    /// </summary>
    [HttpPost("text-and-image")]
    [Authorize]
    public async Task<IActionResult> GenerateTextAndImage(
        [FromForm] string prompt,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken
    )
    {
        // Validasi input
        if (file == null || file.Length == 0 || string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { error = "File atau prompt tidak valid." });

        // Ambil userId dari JWT
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "User tidak valid." });

        // Baca isi file ke memory dan compress jika melebihi 4MB
        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var imageBytes = memoryStream.ToArray();

        if (imageBytes.Length > 4 * 1024 * 1024)
        {
            imageBytes = GeneralHelper.CompressData(imageBytes);
            if (imageBytes.Length > 4 * 1024 * 1024)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        code = Constan.CONST_RES_CD_ERROR,
                        message = Constan.CONST_RES_MESSAGE_ERROR_FILE_SIZE,
                    }
                );
            }
        }

        // Encode file jadi base64 dan siapkan prompt + mime type
        var base64Image = Convert.ToBase64String(imageBytes);
        var mimeType = GeneralHelper.GetMimeType(file.FileName);
        var promptText = Constan.STR_PERSONAL_1_MODEL_GEMINI + prompt;

        // Buat payload JSON untuk API Gemini dengan gambar dan teks
        var body = JsonSerializer.Serialize(
            new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { inlineData = new { mimeType, data = base64Image } },
                            new { text = promptText },
                        },
                    },
                },
                generationConfig = new
                {
                    temperature = 0.95,
                    topP = 0.9,
                    topK = 40,
                    maxOutputTokens = 2048,
                    responseMimeType = "application/json",
                },
            },
            _jsonOptions
        );

        // Kirim request ke Gemini
        // string _endpoint = Constan.CONST_URL_GOOGLE_API_GEMINI_FLASH_15_TEXT_IMAGE + _apiKey;
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client
            .PostAsync(_endpoint, content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return StatusCode((int)response.StatusCode, new { error = err });
        }

        // Parse hasil response
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var json = await JsonSerializer.DeserializeAsync<JsonNode>(stream).ConfigureAwait(false);
        var rawJson = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

        if (string.IsNullOrWhiteSpace(rawJson))
            return StatusCode(502, new { error = "Gemini returned empty response." });

        var finalJson = JsonNode.Parse(rawJson);
        var html = finalJson?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "");

        return Ok(new { html });
    }
}
