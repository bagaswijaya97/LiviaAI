using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;

public class ErrorHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlerMiddleware> _logger;

    public ErrorHandlerMiddleware(RequestDelegate next, ILogger<ErrorHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        // Enable request buffering
        context.Request.EnableBuffering();

        var isMultipart =
            context.Request.ContentType != null
            && context.Request.ContentType.Contains("multipart/form-data");

        string requestBody;
        try
        {
            requestBody = isMultipart
                ? "[Multipart/form-data omitted]"
                : await new StreamReader(
                    context.Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true
                ).ReadToEndAsync();

            context.Request.Body.Position = 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to read request body");
            requestBody = "[Unreadable request body]";
        }

        // Buffer response body
        var originalResponseBody = context.Response.Body;
        using var tempResponseBody = new MemoryStream();
        context.Response.Body = tempResponseBody;

        try
        {
            await _next(context);

            tempResponseBody.Seek(0, SeekOrigin.Begin);
            var responseText = isMultipart
                ? "[Multipart response omitted]"
                : await new StreamReader(tempResponseBody).ReadToEndAsync();
            tempResponseBody.Seek(0, SeekOrigin.Begin);
            await tempResponseBody.CopyToAsync(originalResponseBody);

            // Optionally log here if needed
        }
        catch (Exception)
        {
            stopwatch.Stop();
            context.Response.Body = originalResponseBody; // Restore body

            tempResponseBody.Seek(0, SeekOrigin.Begin);
            var responseText = isMultipart
                ? "[Multipart response omitted]"
                : await new StreamReader(tempResponseBody).ReadToEndAsync();

            var statusCode = (int)HttpStatusCode.InternalServerError;
            var endpoint = context.GetEndpoint();
            var controllerName =
                endpoint
                    ?.Metadata.OfType<ControllerActionDescriptor>()
                    .FirstOrDefault()
                    ?.ControllerName ?? "UnknownController";
            var elapsedFormatted = $"{stopwatch.Elapsed.TotalSeconds:0.00}s";
            var message = GetStatusMessage(statusCode);
            var token =
                context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "")
                ?? "<no_token>";
            var curlCommand =
                $"curl --location '{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}' \\\n"
                + $"--header 'Content-Type: application/json' \\\n"
                + $"--header 'Authorization: Bearer {token}' \\\n"
                + $"--data '{requestBody.Replace("\"", "\\\"")}'";

            _logger.LogError(
                "❌ {StatusCode} {Message} • {ExecuteTime} • {Service} • {Method} • {Path} \nRequest: {Request}\nResponse: {Response}\nCurl: {Curl}",
                statusCode,
                message,
                elapsedFormatted,
                controllerName,
                context.Request.Method,
                context.Request.Path,
                requestBody,
                responseText,
                curlCommand
            );

            if (context.Response.HasStarted || !context.Response.Body.CanWrite)
            {
                _logger.LogWarning(
                    "⚠️ Cannot write error response, response has already started or stream is closed."
                );
                return;
            }

            try
            {
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var errorResponse = new
                {
                    meta_data = new { code = statusCode, message = "Internal Server Error" },
                    data = (object?)null,
                };

                var errorJson = JsonSerializer.Serialize(errorResponse);
                await context.Response.WriteAsync(errorJson);
            }
            catch (Exception writeEx)
            {
                _logger.LogError(
                    writeEx,
                    "❌ Failed to write error response (stream may be closed)."
                );
            }
        }
    }

    private string GetStatusMessage(int statusCode) =>
        statusCode switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Unknown",
        };
}
