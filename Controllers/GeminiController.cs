// Namespace dan library standar untuk autentikasi, JSON, dan dependensi.
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GeminiAIServices.Helpers;
using LiviaAI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

[ApiController]
[Route("api/[controller]")]
public class GeminiController : ControllerBase
{
    // Konstanta API Key dan URL endpoint Gemini, hanya di-set sekali.
    private static readonly string _endpoint1 = Constan.STR_URL_GEMINI_API_1;
    private static readonly string _model = Constan.STR_MODEL_GEMINI;
    private static readonly string _endpoint2 = Constan.STR_URL_GEMINI_API_2;
    private static readonly string _apiKey = Constan.STR_GOOGLE_API_KEY;

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
    private readonly GoogleSheetsLogger _sheetLogger;

    // Constructor untuk inject dependency
    public GeminiController(
        JWTHelper jwtHelper,
        IOptions<JWTOptions> jwtOptions,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        GoogleSheetsLogger sheetLogger
    )
    {
        _jwtHelper = jwtHelper;
        _jwtOptions = jwtOptions.Value;
        _client = httpClientFactory.CreateClient("GeminiClient");
        _cache = memoryCache;
        _sheetLogger = sheetLogger;
    }

    /// <summary>
    /// Endpoint untuk generate konten berbasis teks dari prompt.
    /// </summary>
    [HttpPost("text-only")]
    [Authorize]
    public async Task<IActionResult> GenerateText([FromBody] GeminiRequest request)
    {
        // Validasi request kosong atau prompt kosong
        if (
            request == null
            || string.IsNullOrWhiteSpace(request.prompt)
            || string.IsNullOrWhiteSpace(request.model)
        )
            return BadRequest(new { data = (object?)null });

        // Initialisasi type
        var type = "LiviaTextOnly";

        // Initialisasi model
        var model = request.model;
        if (string.IsNullOrEmpty(model))
            model = _model;

        // Persiapkan URL endpoint
        var _endpoint = _endpoint1 + model + _endpoint2 + _apiKey;

        // Chat history logic
        var chatId = request.session_id ?? "default";
        var chatHistoryKey = $"chat-history:{chatId}";
        var separator = "\n==============================\n";
        var isFirstMessage = false;
        LiviaAI.Models.Gemini.ChatHistory chatHistory;
        if (!_cache.TryGetValue(chatHistoryKey, out chatHistory) || chatHistory == null)
        {
            chatHistory = new LiviaAI.Models.Gemini.ChatHistory { ChatId = chatId };
            isFirstMessage = true;
        }
        // Add new message to history
        chatHistory.Messages.Add(request.prompt);
        // Keep only last 6 messages
        if (chatHistory.Messages.Count > 6)
            chatHistory.Messages = chatHistory
                .Messages.Skip(chatHistory.Messages.Count - 6)
                .ToList();
        // Save updated history back to cache
        _cache.Set(chatHistoryKey, chatHistory, TimeSpan.FromHours(1));
        // Build formatted prompt with separator
        var formattedPrompt = string.Join(separator, chatHistory.Messages);
        var fullPromptText = isFirstMessage
            ? Constan.STR_PERSONAL_1_MODEL_GEMINI + formattedPrompt
            : Constan.STR_FORMAT_RESPONSE_GEMINI + formattedPrompt;
        // Cache key must be unique per session and prompt
        var cacheKey = $"gemini:{chatId}:{request.prompt.GetHashCode()}";

        // Cek apakah hasilnya sudah ada di cache
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml))
            return Ok(new { html = cachedHtml });

        // Persiapkan payload request ke Gemini API
        var body = JsonSerializer.Serialize(
            new
            {
                contents = new[] { new { parts = new[] { new { text = fullPromptText } } } },
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
        Console.WriteLine($"[DEBUG] Gemini rawJson: {rawJson}");
        // If rawJson is a JSON string, extract the html field, otherwise treat as HTML
        string html;
        try
        {
            var parsed = JsonNode.Parse(rawJson);
            html = parsed?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "");
            if (string.IsNullOrWhiteSpace(html))
                html = rawJson;
        }
        catch
        {
            html = rawJson;
        }

        if (string.IsNullOrWhiteSpace(rawJson))
            return StatusCode(502, new { error = "Gemini returned empty response." });

        // Parse hasil HTML dari isi teks
        var finalJson = JsonNode.Parse(rawJson);
        html = finalJson?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "");

        // Ambil informasi token dari usageMetadata
        var usage = json?["usageMetadata"];
        int personaToken = 201; // Token untuk persona
        int promptTokenCount = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int inputToken = Math.Max(0, promptTokenCount - personaToken);
        int outputToken = usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        int totalToken = usage?["totalTokenCount"]?.GetValue<int>() ?? 0;

        // Simpan ke cache untuk 5 menit
        _cache.Set(cacheKey, html, TimeSpan.FromMinutes(5));

        // Logging async tanpa menunggu
        _ = Task.Run(async () =>
        {
            try
            {
                await _sheetLogger.LogAsync(
                    type,
                    request.prompt,
                    html ?? "[null]",
                    personaToken,
                    inputToken,
                    0,
                    outputToken,
                    totalToken,
                    0,
                    model
                );
            }
            catch { }
        });

        // Return response with html and token info
        return Ok(
            new
            {
                meta_data = new { code = 200, message = "OK" },
                data = new
                {
                    html = html,
                    input_token = inputToken,
                    output_token = outputToken,
                    total_token = totalToken,
                },
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
        [FromForm] string model,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken
    )
    {
        if (
            file == null
            || file.Length == 0
            || string.IsNullOrWhiteSpace(prompt)
            || string.IsNullOrWhiteSpace(model)
        )
            return BadRequest(new { error = "Parameter cannot null value." });

        var type = "LiviaTextAndImage";
        if (string.IsNullOrEmpty(type))
            return Unauthorized(new { error = "User tidak valid." });

        // Persiapkan URL endpoint
        var _endpoint = _endpoint1 + model + _endpoint2 + _apiKey;

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var imageBytes = memoryStream.ToArray();

        double fileSizeInMB = Math.Round((double)imageBytes.Length / (1024.0 * 1024.0), 2);

        Console.WriteLine($"File size: {fileSizeInMB} MB");
        if (imageBytes.Length > 4 * 1024 * 1024)
        {
            return BadRequest(
                new
                {
                    success = false,
                    code = Constan.STR_RES_CD_ERROR,
                    message = Constan.STR_RES_MESSAGE_ERROR_FILE_SIZE,
                }
            );
        }

        var base64Image = Convert.ToBase64String(imageBytes);
        var mimeType = GeneralHelper.GetMimeType(file.FileName);
        var fullPrompt = Constan.STR_PERSONAL_1_MODEL_GEMINI + prompt;

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
                            new { text = fullPrompt },
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

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client
            .PostAsync(_endpoint, content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return StatusCode((int)response.StatusCode, new { error = err });
        }

        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var json = await JsonSerializer.DeserializeAsync<JsonNode>(stream).ConfigureAwait(false);
        var rawJson = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

        if (string.IsNullOrWhiteSpace(rawJson))
            return StatusCode(502, new { error = "Gemini returned empty response." });

        var finalJson = JsonNode.Parse(rawJson);
        var html = finalJson?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "");

        var usage = json?["usageMetadata"];
        int personaToken = 201; // [🔧] Atur sesuai panjang persona kamu

        // Estimasi jumlah token dari prompt full (text)
        int textToken = TokenEstimator.EstimateTokens(fullPrompt);
        int promptTokenCount = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int imageToken = Math.Max(0, promptTokenCount - textToken);

        int inputTokenText = Math.Max(0, textToken - personaToken);
        int inputTokenImage = imageToken;
        int outputToken = usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        int totalToken = usage?["totalTokenCount"]?.GetValue<int>() ?? 0;

        // Logging async
        _ = Task.Run(async () =>
        {
            try
            {
                await _sheetLogger.LogAsync(
                    type,
                    prompt,
                    html ?? "[null]",
                    personaToken,
                    inputTokenText,
                    inputTokenImage,
                    outputToken,
                    totalToken,
                    fileSizeInMB,
                    model
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGGING ERROR] {ex.Message}");
            }
        });

        // ✅ Kembalikan data ke client
        return Ok(
            new
            {
                html,
                input_token = personaToken + inputTokenText + inputTokenImage,
                output_token = outputToken,
                total_token = totalToken,
            }
        );
    }
}
