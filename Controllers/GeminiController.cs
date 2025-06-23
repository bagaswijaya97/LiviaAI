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

namespace LiviaAI.Controllers
{
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
        private readonly LiviaAI.Services.IFileStorageService _fileStorageService;
        private readonly ILogger<GeminiController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // Constructor untuk inject dependency
        public GeminiController(
            JWTHelper jwtHelper,
            IOptions<JWTOptions> jwtOptions,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            GoogleSheetsLogger sheetLogger,
            LiviaAI.Services.ChatHistoryService chatHistoryService,
            LiviaAI.Services.IFileStorageService fileStorageService,
            ILogger<GeminiController> logger
        )
        {
            _jwtHelper = jwtHelper;
            _jwtOptions = jwtOptions.Value;
            _httpClientFactory = httpClientFactory;
            _client = httpClientFactory.CreateClient("GeminiClient");
            _cache = memoryCache;
            _sheetLogger = sheetLogger;
            _chatHistoryService = chatHistoryService;
            _fileStorageService = fileStorageService;
            _logger = logger;
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
            var chatId = request.session_id;
            if (string.IsNullOrWhiteSpace(chatId))
            {
                // Coba ambil dari cache HTML (jika sebelumnya pernah ada text-and-image)
                chatId = "CHT-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            }
            var chatHistory = _chatHistoryService.GetOrCreateHistory(chatId);
            _logger.LogInformation(
                "[DEBUG] Isi chatHistory.Turns sebelum prompt: {turns}",
                string.Join(
                    " | ",
                    chatHistory.turns.Select(t =>
                        $"User: {t.user_message} || Livia: {t.livia_response}"
                    )
                )
            );
            var isFirstMessage = chatHistory.turns.Count == 0;

            string fullPromptText;
            if (isFirstMessage)
            {
                // Tambahkan ChatTurn untuk pesan pertama
                chatHistory.turns.Add(new ChatTurn { user_message = request.prompt });
                fullPromptText =
                    Constan.STR_PERSONAL_5_MODEL_GEMINI + "User: " + request.prompt + "\nLivia:";
            }
            else
            {
                chatHistory.turns.Add(new ChatTurn { user_message = request.prompt });
                var persona = Constan.STR_PERSONAL_5_MODEL_GEMINI;
                var dialogue = new StringBuilder();
                foreach (var turn in chatHistory.turns)
                {
                    dialogue.AppendLine($"User: {turn.user_message}");
                    if (!string.IsNullOrWhiteSpace(turn.livia_response))
                    {
                        // Hilangkan prefix "Livia:" jika sudah ada
                        var liviaResp = turn.livia_response.Trim();
                        if (liviaResp.StartsWith("Livia:"))
                        {
                            liviaResp = liviaResp.Substring("Livia:".Length).TrimStart();
                        }
                        dialogue.AppendLine($"Livia: {liviaResp}");
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
                        " || ",
                        chatHistory.turns.Select(t =>
                            $"User: {t.user_message} || {t.livia_response}"
                        )
                    )
            );

            // // Validasi jika pertanyaan dan session_id yang sama
            // if (_chatHistoryService.TryGetCachedHtml(chatId, out string cachedHtml))
            //     return Ok(new { html = cachedHtml });

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
            var json = await JsonSerializer
                .DeserializeAsync<JsonNode>(stream)
                .ConfigureAwait(false);
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
                        Console.WriteLine(
                            $"[ERROR] Gagal parse rawJson sebagai JSON: {ex.Message}"
                        );
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
                // Set Livia response for the first turn (sudah ditambahkan sebelumnya)
                if (chatHistory.turns.Count > 0)
                {
                    chatHistory.turns[chatHistory.turns.Count - 1].livia_response =
                        html ?? string.Empty;
                }
            }
            else
            {
                html = (html ?? string.Empty).TrimStart();
                if ((html ?? string.Empty).StartsWith("Livia:"))
                {
                    html = (html ?? string.Empty).Substring("Livia:".Length).TrimStart();
                }
                html = "Livia: " + (html ?? string.Empty);
                // Set Livia response for the last turn
                if (chatHistory.turns.Count > 0)
                {
                    chatHistory.turns[chatHistory.turns.Count - 1].livia_response = html;
                }
            }
            // Parse hasil HTML dari isi teks
            var finalJson = JsonNode.Parse(rawJson);
            if (finalJson != null && finalJson["html"] != null)
                html =
                    finalJson["html"]!.ToString()?.Replace("\n", "")?.Replace("\r", "")
                    ?? string.Empty;

            // Ambil informasi token dari usageMetadata
            var usage = json?["usageMetadata"];
            int personaToken = 599; // Token untuk persona
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
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOGGING ERROR] {ex.Message}");
                }
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
            double fileSizeInMB =
                file != null ? Math.Round((double)file.Length / (1024 * 1024), 2) : 0;

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
            var chatId = session_id;
            if (string.IsNullOrWhiteSpace(chatId))
            {
                chatId = HttpContext.Request.Form["session_id"].FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(chatId))
            {
                chatId = "CHT-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            }
            var chatHistory = _chatHistoryService.GetOrCreateHistory(chatId);
            _logger.LogInformation(
                "[DEBUG] Isi chatHistory.Turns sebelum prompt: {turns}",
                string.Join(
                    " | ",
                    chatHistory.turns.Select(t =>
                        $"User: {t.user_message} || Livia: {t.livia_response}"
                    )
                )
            );
            var isFirstMessage = chatHistory.turns.Count == 0;

            string fullPrompt;

            // Simpan file ke storage dan buat file attachment
            string savedFilePath;
            try
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                savedFilePath = await _fileStorageService.SaveFileAsync(
                    file.FileName,
                    memoryStream.ToArray(),
                    mimeType
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save uploaded file: {fileName}", file.FileName);
                return StatusCode(500, new { error = "Failed to save uploaded file" });
            }

            var fileAttachment = new FileAttachment
            {
                file_name = file.FileName,
                file_type = GetFileType(mimeType),
                file_size = file.Length,
                mime_type = mimeType,
                file_url = _fileStorageService.GetFileUrl(savedFilePath), // Menyimpan URL file
            };

            if (isFirstMessage)
            {
                // Tambahkan ChatTurn untuk pesan pertama dengan attachment
                chatHistory.turns.Add(
                    new ChatTurn
                    {
                        user_message = prompt,
                        attachments = new List<FileAttachment> { fileAttachment },
                    }
                );
                fullPrompt = Constan.STR_PERSONAL_5_MODEL_GEMINI + "User: " + prompt + "\nLivia:";
            }
            else
            {
                chatHistory.turns.Add(
                    new ChatTurn
                    {
                        user_message = prompt,
                        attachments = new List<FileAttachment> { fileAttachment },
                    }
                );
                var persona = Constan.STR_PERSONAL_5_MODEL_GEMINI;
                var dialogue = new StringBuilder();
                // Ambil hanya 2 percakapan terakhir (jika ada)
                var lastTurns =
                    chatHistory.turns.Count > 2
                        ? chatHistory.turns.GetRange(chatHistory.turns.Count - 2, 2)
                        : chatHistory.turns;
                foreach (var turn in chatHistory.turns)
                {
                    dialogue.AppendLine($"User: {turn.user_message}");
                    if (!string.IsNullOrWhiteSpace(turn.livia_response))
                    {
                        dialogue.AppendLine($" {turn.livia_response}");
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
                        " || ",
                        chatHistory.turns.Select(t =>
                            $"User: {t.user_message} || Livia: {t.livia_response}"
                        )
                    )
            );
            // // Validasi jika pertanyaan dan session_id yang sama
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
                                new
                                {
                                    inlineData = new { mimeType = mimeType, data = base64Image },
                                },
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
            var json = await JsonSerializer
                .DeserializeAsync<JsonNode>(stream)
                .ConfigureAwait(false);
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
            if (chatHistory.turns.Count > 0)
            {
                chatHistory.turns[chatHistory.turns.Count - 1].livia_response = "Livia: " + html;
            }
            _chatHistoryService.SaveHistory(chatId, chatHistory);
            _chatHistoryService.SaveCachedHtml(chatId, html);

            // === TOKEN USAGE ===
            var usage = json?["usageMetadata"];
            int personaToken = 599;
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
                        chat_id = chatId,
                        file_info = new
                        {
                            file_name = file.FileName,
                            file_type = GetFileType(mimeType),
                            file_size = file.Length,
                            mime_type = mimeType,
                            file_url = _fileStorageService.GetFileUrl(savedFilePath),
                        },
                    },
                }
            );
        }

        /// <summary>
        /// Endpoint untuk mendapatkan daftar semua chat histories
        /// </summary>
        [HttpGet("chat-histories")]
        [Authorize]
        public async Task<IActionResult> GetChatHistories()
        {
            try
            {
                // Log request
                _logger.LogInformation(
                    "[API] GetChatHistories called at {timestamp}",
                    DateTime.UtcNow
                );

                // Get user info from JWT token
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown";

                _logger.LogInformation(
                    "[API] GetChatHistories - User: {userId}, Email: {userEmail}",
                    userId,
                    userEmail
                );

                // Get all histories from service
                var histories = _chatHistoryService.GetAllHistories();

                var response = new
                {
                    success = true,
                    message = "Chat histories retrieved successfully",
                    data = histories,
                    timestamp = DateTime.UtcNow,
                    total = histories.Count,
                };

                // Log to Google Sheets
                try
                {
                    await _sheetLogger.LogAsync(
                        "GetChatHistories",
                        $"User: {userEmail}",
                        JsonSerializer.Serialize(response, _jsonOptions),
                        0, // persona token
                        0, // input text token
                        0, // input image token
                        0, // output token
                        0, // total token
                        0, // file size
                        "API-GetChatHistories"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError("[ERROR] Failed to log to Google Sheets: {error}", ex.Message);
                }

                _logger.LogInformation(
                    "[API] GetChatHistories completed successfully. Total histories: {count}",
                    histories.Count
                );
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ERROR] GetChatHistories failed: {error}", ex.Message);
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "Internal server error occurred while retrieving chat histories",
                        error = ex.Message,
                        timestamp = DateTime.UtcNow,
                    }
                );
            }
        }

        /// <summary>
        /// Endpoint untuk mendapatkan detail chat history berdasarkan session ID
        /// </summary>
        [HttpGet("chat-history/{sessionId}")]
        [Authorize]
        public async Task<IActionResult> GetChatHistoryDetail(string sessionId)
        {
            try
            {
                // Validasi session ID
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _logger.LogWarning("[API] GetChatHistoryDetail called with empty sessionId");
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = "Session ID is required",
                            timestamp = DateTime.UtcNow,
                        }
                    );
                }

                // Log request
                _logger.LogInformation(
                    "[API] GetChatHistoryDetail called for sessionId: {sessionId} at {timestamp}",
                    sessionId,
                    DateTime.UtcNow
                );

                // Get user info from JWT token
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown";

                _logger.LogInformation(
                    "[API] GetChatHistoryDetail - User: {userId}, Email: {userEmail}, SessionId: {sessionId}",
                    userId,
                    userEmail,
                    sessionId
                );

                // Get history by session ID
                var chatHistory = _chatHistoryService.GetHistoryBySessionId(sessionId);

                if (chatHistory == null)
                {
                    _logger.LogWarning(
                        "[API] Chat history not found for sessionId: {sessionId}",
                        sessionId
                    );
                    return NotFound(
                        new
                        {
                            success = false,
                            message = $"Chat history not found for session ID: {sessionId}",
                            timestamp = DateTime.UtcNow,
                        }
                    );
                }

                // Create detailed response
                var detailResponse = new ChatHistoryDetailResponse
                {
                    session_id = sessionId,
                    chat_id = chatHistory.chat_id,
                    turns = chatHistory.turns,
                    created_at = chatHistory.created_at,
                    last_updated = chatHistory.last_updated,
                    total_turns = chatHistory.turns.Count,
                };

                var response = new
                {
                    success = true,
                    message = "Chat history detail retrieved successfully",
                    data = detailResponse,
                    timestamp = DateTime.UtcNow,
                };

                // Log to Google Sheets
                try
                {
                    await _sheetLogger.LogAsync(
                        "GetChatHistoryDetail",
                        $"User: {userEmail}, SessionId: {sessionId}",
                        JsonSerializer.Serialize(response, _jsonOptions),
                        0, // persona token
                        0, // input text token
                        0, // input image token
                        0, // output token
                        0, // total token
                        0, // file size
                        "API-GetChatHistoryDetail"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError("[ERROR] Failed to log to Google Sheets: {error}", ex.Message);
                }

                _logger.LogInformation(
                    "[API] GetChatHistoryDetail completed successfully for sessionId: {sessionId}. Total turns: {count}",
                    sessionId,
                    chatHistory.turns.Count
                );
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "[ERROR] GetChatHistoryDetail failed for sessionId: {sessionId}. Error: {error}",
                    sessionId,
                    ex.Message
                );
                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        message = "Internal server error occurred while retrieving chat history detail",
                        error = ex.Message,
                        sessionId = sessionId,
                        timestamp = DateTime.UtcNow,
                    }
                );
            }
        }

        /// <summary>
        /// Helper method untuk menentukan file type berdasarkan MIME type
        /// </summary>
        private string GetFileType(string mimeType)
        {
            return mimeType switch
            {
                var mt when mt.StartsWith("image/") => "image",
                "application/pdf" => "pdf",
                var mt when mt.StartsWith("text/") => "document",
                var mt when mt.StartsWith("application/vnd.openxmlformats-officedocument") =>
                    "document",
                var mt when mt.StartsWith("application/msword") => "document",
                var mt when mt.StartsWith("application/vnd.ms-excel") => "document",
                var mt when mt.StartsWith("application/vnd.ms-powerpoint") => "document",
                _ => "other",
            };
        }
    }
}
