// Namespace dan library standar untuk autentikasi, JSON, dan dependensi.
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GeminiAIServices.Helpers;
using LiviaAI.Helpers;
using LiviaAI.Models.Gemini;
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
    private readonly LiviaAI.Services.ChatHistoryService _chatHistoryService;

    // Constructor untuk inject dependency
    public GeminiController(
        JWTHelper jwtHelper,
        IOptions<JWTOptions> jwtOptions,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        GoogleSheetsLogger sheetLogger,
        LiviaAI.Services.ChatHistoryService chatHistoryService
    )
    {
        _jwtHelper = jwtHelper;
        _jwtOptions = jwtOptions.Value;
        _client = httpClientFactory.CreateClient("GeminiClient");
        _cache = memoryCache;
        _sheetLogger = sheetLogger;
        _chatHistoryService = chatHistoryService;
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

        // Chat history logic untuk text-only (refactored)
        var chatId = request.session_id ?? "CHT-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        var chatHistory = _chatHistoryService.GetOrCreateHistory(chatId);
        var isFirstMessage = chatHistory.Turns.Count == 0;

        string fullPromptText;
        if (isFirstMessage)
        {
            fullPromptText =
                Constan.STR_PERSONAL_1_MODEL_GEMINI + "User: " + request.prompt + "\nLivia:";
        }
        else
        {
            chatHistory.Turns.Add(new ChatTurn { UserMessage = request.prompt });
            var persona = Constan.STR_PERSONAL_1_MODEL_GEMINI;
            var dialogue = new StringBuilder();
            foreach (var turn in chatHistory.Turns)
            {
                dialogue.AppendLine($"User: {turn.UserMessage}");
                if (!string.IsNullOrWhiteSpace(turn.LiviaResponse))
                {
                    dialogue.AppendLine($" {turn.LiviaResponse}");
                }
                else
                {
                    dialogue.AppendLine("Livia:");
                }
            }
            fullPromptText = persona + dialogue.ToString();
        }
        _chatHistoryService.SaveHistory(chatId, chatHistory);
        Console.WriteLine(
            $"[DEBUG] History Chat : gemini:history:{chatId} - "
                + string.Join(
                    " | ",
                    chatHistory.Turns.Select(t =>
                        $"User: {t.UserMessage} || Livia: {t.LiviaResponse}"
                    )
                )
        );
        if (_chatHistoryService.TryGetCachedHtml(chatId, out string cachedHtml))
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
        string html = string.Empty;
        try
        {
            if (!string.IsNullOrEmpty(rawJson))
            {
                try
                {
                    var parsedNode = JsonNode.Parse(rawJson);
                    var htmlNode = parsedNode?["html"];
                    if (htmlNode != null)
                    {
                        html = htmlNode.ToString().Replace("\n", "").Replace("\r", "");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Gagal parse rawJson sebagai JSON: {ex.Message}");
                    html = rawJson;
                }
                if (string.IsNullOrWhiteSpace(html))
                    html = rawJson ?? string.Empty;
            }
            else
            {
                html = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception parsing html: {ex.Message}");
            html = rawJson ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(rawJson))
            return StatusCode(502, new { error = "Gemini returned empty response." });

        // Tambahkan flag "Livia:" ke response, hanya untuk chat pertama tambahkan sapaan
        if (isFirstMessage)
        {
            html = "Livia: " + html;
            // Set first Livia response in history
            chatHistory.Turns.Add(
                new ChatTurn { UserMessage = request.prompt, LiviaResponse = html ?? string.Empty }
            );
        }
        else
        {
            // Remove greeting from subsequent responses if present
            if (!string.IsNullOrEmpty(html) && html.Contains("Hai! Aku Livia"))
            {
                int idx = html.IndexOf("Hai! Aku Livia");
                int endIdx = html.IndexOf(".", idx);
                if (endIdx != -1)
                {
                    html = html.Remove(idx, endIdx - idx + 1).TrimStart();
                }
                else
                {
                    html = html.Remove(idx, "Hai! Aku Livia".Length).TrimStart();
                }
            }
            html = (html ?? string.Empty).TrimStart();
            if ((html ?? string.Empty).StartsWith("Livia:"))
            {
                html = (html ?? string.Empty).Substring("Livia:".Length).TrimStart();
            }
            html = "Livia: " + (html ?? string.Empty);
            // Set Livia response for the last turn
            if (chatHistory.Turns.Count > 0)
            {
                chatHistory.Turns[chatHistory.Turns.Count - 1].LiviaResponse = html;
            }
        }
        // Parse hasil HTML dari isi teks
        var finalJson = JsonNode.Parse(rawJson);
        if (finalJson != null && finalJson["html"] != null)
            html =
                finalJson["html"]!.ToString()?.Replace("\n", "")?.Replace("\r", "") ?? string.Empty;

        // Save updated history back to cache (after response)

        // Ambil informasi token dari usageMetadata
        var usage = json?["usageMetadata"];
        int personaToken = 200; // Token untuk persona
        int promptTokenCount = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int inputToken = Math.Max(0, promptTokenCount - personaToken);
        int outputToken =
            usage?["candidatesTokenCount"]?.GetValue<int>()
                + usage?["thoughtsTokenCount"]?.GetValue<int>()
            ?? 0;
        int totalToken = usage?["totalTokenCount"]?.GetValue<int>() ?? 0;

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
        [FromForm] string session_id,
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
        var _endpoint = _endpoint1 + model + _endpoint2 + _apiKey;

        // Calculate file size in MB for logging
        double fileSizeInMB = file != null ? Math.Round((double)file.Length / (1024 * 1024), 2) : 0;

        // Convert uploaded file to base64 string
        string base64Image = string.Empty;
        using (var ms = new MemoryStream())
        {
            await file!.CopyToAsync(ms, cancellationToken);
            base64Image = Convert.ToBase64String(ms.ToArray());
        }
        // Get mime type from uploaded file
        string mimeType = file!.ContentType;
        // Chat history logic untuk text-and-image (refactored)
        var chatId =
            HttpContext.Request.Form["session_id"].FirstOrDefault()
            ?? "CHT-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        var chatHistory = _chatHistoryService.GetOrCreateHistory(chatId);
        var isFirstMessage = chatHistory.Turns.Count == 0;

        string fullPrompt;
        if (isFirstMessage)
        {
            fullPrompt = Constan.STR_PERSONAL_1_MODEL_GEMINI + "User: " + prompt + "\nLivia:";
        }
        else
        {
            chatHistory.Turns.Add(new ChatTurn { UserMessage = prompt });
            var persona = Constan.STR_PERSONAL_1_MODEL_GEMINI;
            var dialogue = new StringBuilder();
            foreach (var turn in chatHistory.Turns)
            {
                dialogue.AppendLine($"User: {turn.UserMessage}");
                if (!string.IsNullOrWhiteSpace(turn.LiviaResponse))
                {
                    dialogue.AppendLine($" {turn.LiviaResponse}");
                }
                else
                {
                    dialogue.AppendLine("Livia:");
                }
            }
            fullPrompt = persona + dialogue.ToString();
        }
        Console.WriteLine(
            $"[DEBUG] History Chat : gemini:history:{chatId} - "
                + string.Join(
                    " | ",
                    chatHistory.Turns.Select(t =>
                        $"User: {t.UserMessage} || Livia: {t.LiviaResponse}"
                    )
                )
        );
        // if (_chatHistoryService.TryGetCachedHtml(chatId, out string cachedHtml))
        //     return Ok(new { html = cachedHtml });

        // Persiapkan payload request ke Gemini API
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

        // === PARSE HTML FROM JSON RESPONSE ===
        string html = string.Empty;
        try
        {
            var finalJson = JsonNode.Parse(rawJson);
            html =
                finalJson?["html"]?.ToString()?.Replace("\n", "")?.Replace("\r", "")
                ?? string.Empty;
        }
        catch
        {
            html = rawJson ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(html))
            html = "[no content returned]";

        // === UPDATE CHAT HISTORY ===
        if (chatHistory.Turns.Count > 0)
        {
            chatHistory.Turns[chatHistory.Turns.Count - 1].LiviaResponse = "Livia: " + html;
        }
        _chatHistoryService.SaveHistory(chatId, chatHistory);
        _chatHistoryService.SaveCachedHtml(chatId, html);

        // === TOKEN USAGE ===
        var usage = json?["usageMetadata"];
        int personaToken = 201;
        int textToken = TokenEstimator.EstimateTokens(fullPrompt);
        int promptTokenCount = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int imageToken = Math.Max(0, promptTokenCount - textToken);
        int inputTokenText = Math.Max(0, textToken - personaToken);
        int inputTokenImage = imageToken;
        int outputToken =
            usage?["candidatesTokenCount"]?.GetValue<int>()
                + usage?["thoughtsTokenCount"]?.GetValue<int>()
            ?? 0;
        int totalToken = usage?["totalTokenCount"]?.GetValue<int>() ?? 0;

        // === ASYNC LOGGING ===
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

        // === RETURN TO CLIENT ===
        return Ok(
            new
            {
                meta_data = new { code = 200, message = "OK" },
                data = new
                {
                    html,
                    input_token = personaToken + inputTokenText + inputTokenImage,
                    output_token = outputToken,
                    total_token = totalToken,
                },
            }
        );
    }
}
