using System.Text.Json;

namespace EigenfocusApi.Auth;

/// <summary>
/// Enforces the <c>X-API-Key</c> header against the configured <c>ApiKey</c> value.
/// Requests to <c>/health</c> are allowed through unauthenticated so liveness probes do not require the key.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-API-Key";
    private readonly RequestDelegate _next;
    private readonly string _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _expectedKey = configuration["ApiKey"] ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(_expectedKey))
        {
            await WriteUnauthorized(context, "Server API key is not configured.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !string.Equals(provided.ToString(), _expectedKey, StringComparison.Ordinal))
        {
            await WriteUnauthorized(context, "Missing or invalid API key.");
            return;
        }

        await _next(context);
    }

    private static bool IsAnonymousPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static Task WriteUnauthorized(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(
            new { error = "Unauthorized", detail },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return context.Response.WriteAsync(payload);
    }
}
