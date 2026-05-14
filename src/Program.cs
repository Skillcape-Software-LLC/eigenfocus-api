using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using EigenfocusApi.Auth;
using EigenfocusApi.Data;
using EigenfocusApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var detail = feature?.Error.Message ?? "An unexpected error occurred.";
        app.Logger.LogError(feature?.Error, "Unhandled exception");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new { error = "Server error", detail },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    });
});

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

ReadEndpoints.Map(app);
IssueEndpoints.Map(app);
CommentEndpoints.Map(app);

app.Run();
