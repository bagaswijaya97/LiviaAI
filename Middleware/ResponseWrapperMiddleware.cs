using System.Net;
using System.Text.Json;

public class ResponseWrapperMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        await _next(context);

        memStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(memStream).ReadToEndAsync();
        memStream.Seek(0, SeekOrigin.Begin);

        context.Response.Body = originalBody;

        // 🧠 Jangan bungkus ulang jika sudah ada "meta_data" dan "data"
        if (responseBody.Contains("\"meta_data\"") && responseBody.Contains("\"data\""))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody);
            return;
        }

        // Non-JSON (ex: file/image), biarkan
        if (!context.Response.ContentType?.Contains("application/json") ?? true)
        {
            await memStream.CopyToAsync(originalBody);
            return;
        }

        var statusCode = context.Response.StatusCode;
        string statusMessage = GetReasonPhrase((HttpStatusCode)statusCode);

        object? parsedData = null;
        try
        {
            parsedData = JsonSerializer.Deserialize<object>(responseBody);
        }
        catch
        { /* ignore parsing failure */
        }

        var result = new
        {
            meta_data = new { code = statusCode, message = statusMessage },
            data = statusCode >= 200 && statusCode < 300 ? parsedData : null,
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(result));
    }

    private static string GetReasonPhrase(HttpStatusCode code)
    {
        return Enum.IsDefined(typeof(HttpStatusCode), code)
            ? Enum.GetName(typeof(HttpStatusCode), code) ?? "Unknown"
            : "Unknown";
    }
}
