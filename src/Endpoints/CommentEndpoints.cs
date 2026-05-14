using System.Data;
using System.Text.Json;
using Dapper;
using EigenfocusApi.Data;
using EigenfocusApi.Models;

namespace EigenfocusApi.Endpoints;

public static class CommentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/issues/{issueId:long}/comments", CreateComment);
    }

    private static async Task<IResult> CreateComment(
        long issueId,
        JsonElement body,
        IDbConnectionFactory factory)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ApiResults.BadRequest("Request body must be a JSON object.");

        if (!body.TryGetProperty("content", out var contentProp)
            || contentProp.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(contentProp.GetString()))
            return ApiResults.BadRequest("content is required.");

        if (!body.TryGetProperty("authorId", out var authorProp)
            || authorProp.ValueKind != JsonValueKind.Number
            || !authorProp.TryGetInt64(out var authorId))
            return ApiResults.BadRequest("authorId is required.");

        var content = contentProp.GetString()!;

        using var conn = factory.Create();

        if (!await IssueExists(conn, issueId))
            return ApiResults.NotFound($"Issue {issueId} does not exist.");
        if (!await UserExists(conn, authorId))
            return ApiResults.Conflict($"authorId {authorId} does not exist.");

        using var tx = conn.BeginTransaction();
        var now = DateTime.UtcNow;

        var newId = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO issue_comments (content, issue_id, author_id, created_at, updated_at)
              VALUES (@content, @issueId, @authorId, @now, @now)
              RETURNING id",
            new { content, issueId, authorId, now }, tx);

        await conn.ExecuteAsync(
            "UPDATE issues SET comments_count = comments_count + 1, updated_at = @now WHERE id = @issueId",
            new { issueId, now }, tx);

        tx.Commit();

        var comment = await conn.QuerySingleAsync<IssueComment>(
            @"SELECT id, content, issue_id, author_id, created_at, updated_at
              FROM issue_comments WHERE id = @newId",
            new { newId });

        return Results.Created($"/api/issues/{issueId}/comments/{newId}", comment);
    }

    private static async Task<bool> IssueExists(IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM issues WHERE id = @id", new { id }) > 0;

    private static async Task<bool> UserExists(IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM users WHERE id = @id", new { id }) > 0;
}
