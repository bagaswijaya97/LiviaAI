using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Controllers;
using Serilog;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger
    )
    {
        _next = next;
        _logger = logger;
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

    public async Task Invoke(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        context.Request.EnableBuffering();
        var isMultipart =
            context.Request.ContentType != null
            && context.Request.ContentType.Contains("multipart/form-data");
        var requestBody = isMultipart
            ? "[Multipart/form-data omitted]"
            : await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var originalBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        stopwatch.Stop();

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseText = isMultipart
            ? "[Multipart response omitted]"
            : await new StreamReader(context.Response.Body).ReadToEndAsync();
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        var statusCode = context.Response.StatusCode;
        var endpoint = context.GetEndpoint();
        var controllerName =
            endpoint?.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault()?.ControllerName
            ?? "UnknownController";
        var elapsedFormatted = $"{stopwatch.Elapsed.TotalSeconds:0.00}ms";
        var message = GetStatusMessage(statusCode);
        var token =
            context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "")
            ?? "<no_token>";
        var curlCommand =
            $"curl --location '{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}' \\\n--header 'Content-Type: application/json' \\\n--header 'Authorization: Bearer {token}' \\\n--data '{requestBody.Replace("\"", "\\\"")}'";

        if (statusCode >= 500)
        {
            Log.Error(
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
        }
        else if (statusCode >= 400)
        {
            Log.Warning(
                "⚠️ {StatusCode} {Message} • {ExecuteTime} • {Service} • {Method} • {Path} \nRequest: {Request}\nResponse: {Response}\nCurl: {Curl}",
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
        }
        else
        {
            Log.Information(
                "✅ {StatusCode} {Message} • {ExecuteTime} • {Service} • {Method} • {Path} \nRequest: {Request}\nResponse: {Response}\nCurl: {Curl}",
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
        }

        await responseBody.CopyToAsync(originalBody);
    }
}
