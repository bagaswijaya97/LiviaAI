using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiviaAI.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// Setup Logging with Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.WithProperty("Application", "LiviaAI") // ini penting!
    .WriteTo.Console()
    // .WriteTo.Seq("http://localhost:5341", apiKey: "obff1nkQoA47FLTaWoC9")
    .WriteTo.Seq("http://localhost:5341") // untuk local
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Default logging
builder.Host.UseSerilog();

// ✅ Konfigurasi JWTOptions
builder.Services.Configure<JWTOptions>(builder.Configuration.GetSection("JWT"));
builder.Services.AddSingleton<JWTHelper>();

// ✅ CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    );
});

// ✅ JSON Option
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// ✅ Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Wajib untuk IHttpClientFactory
builder.Services.AddHttpClient();

// Ambil JWTOptions hanya sekali
var jwtOptions =
    builder.Configuration.GetSection("JWT").Get<JWTOptions>()
    ?? throw new InvalidOperationException("JWT options not found.");
string strJWTKey = jwtOptions.Key;

// ✅ JWT Authentication
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };

        // ✅ Formatkan expired/invalid token
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();

                context.HttpContext.Response.StatusCode = 401;
                context.HttpContext.Response.ContentType = "application/json";

                // Log.Warning(context.HttpContext.Response.StatusCode + " - " +"Token is invalid or expired.", context.HttpContext.Connection.RemoteIpAddress?.ToString());

                var response = new
                {
                    meta_data = new { code = 401, message = "Token is invalid or expired." },
                    data = (object?)null,
                };

                return context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(response));
            },
        };
    });

// ✅ Setup Timeout untuk Gemini API
builder.Services.AddHttpClient(
    "GeminiClient",
    client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60); // adjustable
    }
);

// ✅ Setup Cahce untuk Gemini API
builder.Services.AddMemoryCache();

// ✅ Google Sheets Logger
builder.Services.AddSingleton(provider =>
{
    var credentialPath = "monitoringliviaai-486c0c7bbf4c.json"; // letakkan file JSON ini di root project
    var spreadsheetId = "13VhYocw5otSEtHV5fpvcfHNYoRnqvEd14zqcDishhY0";
    return new GoogleSheetsLogger(credentialPath, spreadsheetId);
});

var app = builder.Build();

// ✅ Middleware global error dan wrapper
app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseMiddleware<ResponseWrapperMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// ✅ Middleware CORS
app.UseCors("AllowAll");

// ✅ Middleware untuk validasi secreetKey (tanpa override ResponseWrapper)
app.Use(
    async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await next();
            return;
        }

        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var tokenWithSecret = authHeader["Bearer ".Length..];
            var parts = tokenWithSecret.Split('.');

            if (parts.Length >= 4)
            {
                var secreetKey = parts[^1];
                if (secreetKey != strJWTKey)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";

                    // Log.Error("Unexpected error when processing {Endpoint}", context.Request.Path);

                    var response = new
                    {
                        meta_data = new { code = 401, message = "Invalid secret key" },
                        data = (object?)null,
                    };

                    await context.Response.WriteAsync(JsonSerializer.Serialize(response));
                    return;
                }

                var cleanToken = string.Join('.', parts.Take(3));
                context.Request.Headers["Authorization"] = $"Bearer {cleanToken}";
            }
        }

        await next();
    }
);

app.UseAuthentication();
app.UseAuthorization();

// ✅ Swagger
app.UseSwagger();
app.UseSwaggerUI();

// ✅ Map controller
app.MapControllers();

app.Run();
