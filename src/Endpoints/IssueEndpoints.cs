using System.Data;
using System.Text.Json;
using Dapper;
using EigenfocusApi.Data;
using EigenfocusApi.Models;

namespace EigenfocusApi.Endpoints;

public static class IssueEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/projects/{projectId:long}/issues", CreateIssue);
        api.MapPut("/issues/{id:long}", UpdateIssue);
        api.MapPatch("/issues/{id:long}/custom-fields", UpdateCustomFields);
        api.MapPost("/issues/{id:long}/finish", FinishIssue);
        api.MapPost("/issues/{id:long}/unfinish", UnfinishIssue);
    }

    private static async Task<IResult> CreateIssue(
        long projectId,
        JsonElement body,
        IDbConnectionFactory factory)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ApiResults.BadRequest("Request body must be a JSON object.");

        if (!TryGetString(body, "title", out var title) || string.IsNullOrWhiteSpace(title))
            return ApiResults.BadRequest("title is required.");

        if (!TryGetLong(body, "statusId", out var statusId) || statusId is null)
            return ApiResults.BadRequest("statusId is required.");
        if (!TryGetLong(body, "typeId", out var typeId) || typeId is null)
            return ApiResults.BadRequest("typeId is required.");

        TryGetString(body, "description", out var description);
        TryGetLong(body, "assigneeId", out var assigneeId);
        TryGetLong(body, "parentId", out var parentId);
        TryGetDate(body, "dueDate", out var dueDate);
        TryGetDate(body, "startDate", out var startDate);
        TryGetDate(body, "endDate", out var endDate);
        var labelIds = ReadLabelIds(body);

        using var conn = factory.Create();

        if (!await ProjectExists(conn, projectId))
            return ApiResults.NotFound($"Project {projectId} does not exist.");
        if (!await BelongsToProject(conn, "issue_statuses", statusId.Value, projectId))
            return ApiResults.Conflict($"statusId {statusId} does not belong to project {projectId}.");
        if (!await BelongsToProject(conn, "issue_types", typeId.Value, projectId))
            return ApiResults.Conflict($"typeId {typeId} does not belong to project {projectId}.");
        if (assigneeId.HasValue && !await UserExists(conn, assigneeId.Value))
            return ApiResults.Conflict($"assigneeId {assigneeId} does not exist.");
        foreach (var labelId in labelIds)
        {
            if (!await BelongsToProject(conn, "issue_labels", labelId, projectId))
                return ApiResults.Conflict($"labelId {labelId} does not belong to project {projectId}.");
        }

        using var tx = conn.BeginTransaction();

        var nextRank = await conn.ExecuteScalarAsync<long?>(
            "SELECT MAX(rank) FROM issues WHERE project_id = @projectId",
            new { projectId }, tx) ?? 0L;
        nextRank += 1;

        var now = DateTime.UtcNow;
        var newId = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO issues
                (title, description, project_id, status_id, type_id, assignee_id, parent_id,
                 rank, due_date, start_date, end_date, custom_fields, comments_count,
                 created_at, updated_at)
              VALUES
                (@title, @description, @projectId, @statusId, @typeId, @assigneeId, @parentId,
                 @rank, @dueDate, @startDate, @endDate, '{}', 0,
                 @now, @now)
              RETURNING id",
            new
            {
                title,
                description,
                projectId,
                statusId,
                typeId,
                assigneeId,
                parentId,
                rank = nextRank,
                dueDate,
                startDate,
                endDate,
                now,
            }, tx);

        if (labelIds.Count > 0)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO issue_label_links (issue_id, issue_label_id, created_at, updated_at)
                  VALUES (@issueId, @labelId, @now, @now)",
                labelIds.Select(labelId => new { issueId = newId, labelId, now }),
                tx);
        }

        tx.Commit();

        var issue = await IssueRepository.LoadByIdAsync(conn, newId);
        return Results.Created($"/api/issues/{newId}", issue);
    }

    private static async Task<IResult> UpdateIssue(
        long id,
        JsonElement body,
        IDbConnectionFactory factory)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ApiResults.BadRequest("Request body must be a JSON object.");

        using var conn = factory.Create();
        var projectIdNullable = await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT project_id FROM issues WHERE id = @id",
            new { id });
        if (projectIdNullable is null)
            return ApiResults.NotFound($"Issue {id} does not exist.");
        var projectId = projectIdNullable.Value;

        var updates = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);

        if (TryGetProperty(body, "title", out var titleProp))
        {
            if (titleProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(titleProp.GetString()))
                return ApiResults.BadRequest("title cannot be blank.");
            updates.Add("title = @title");
            p.Add("title", titleProp.GetString());
        }
        if (TryGetProperty(body, "description", out var descProp))
        {
            updates.Add("description = @description");
            p.Add("description", descProp.ValueKind == JsonValueKind.Null ? null : descProp.GetString());
        }
        if (TryGetProperty(body, "statusId", out var statusProp))
        {
            if (!TryReadLong(statusProp, out var statusId) || statusId is null)
                return ApiResults.BadRequest("statusId must be a number.");
            if (!await BelongsToProject(conn, "issue_statuses", statusId.Value, projectId))
                return ApiResults.Conflict($"statusId {statusId} does not belong to project {projectId}.");
            updates.Add("status_id = @statusId");
            p.Add("statusId", statusId.Value);
        }
        if (TryGetProperty(body, "typeId", out var typeProp))
        {
            if (!TryReadLong(typeProp, out var typeId) || typeId is null)
                return ApiResults.BadRequest("typeId must be a number.");
            if (!await BelongsToProject(conn, "issue_types", typeId.Value, projectId))
                return ApiResults.Conflict($"typeId {typeId} does not belong to project {projectId}.");
            updates.Add("type_id = @typeId");
            p.Add("typeId", typeId.Value);
        }
        if (TryGetProperty(body, "assigneeId", out var assigneeProp))
        {
            if (!TryReadLong(assigneeProp, out var assigneeId))
                return ApiResults.BadRequest("assigneeId must be a number or null.");
            if (assigneeId.HasValue && !await UserExists(conn, assigneeId.Value))
                return ApiResults.Conflict($"assigneeId {assigneeId} does not exist.");
            updates.Add("assignee_id = @assigneeId");
            p.Add("assigneeId", assigneeId);
        }
        if (TryGetProperty(body, "dueDate", out var dueProp))
        {
            if (!TryReadDate(dueProp, out var dueDate))
                return ApiResults.BadRequest("dueDate must be a date string or null.");
            updates.Add("due_date = @dueDate");
            p.Add("dueDate", dueDate);
        }
        if (TryGetProperty(body, "startDate", out var startProp))
        {
            if (!TryReadDate(startProp, out var startDate))
                return ApiResults.BadRequest("startDate must be a date string or null.");
            updates.Add("start_date = @startDate");
            p.Add("startDate", startDate);
        }
        if (TryGetProperty(body, "endDate", out var endProp))
        {
            if (!TryReadDate(endProp, out var endDate))
                return ApiResults.BadRequest("endDate must be a date string or null.");
            updates.Add("end_date = @endDate");
            p.Add("endDate", endDate);
        }

        var hasLabelIds = TryGetProperty(body, "labelIds", out var labelIdsProp);
        List<long>? newLabelIds = null;
        if (hasLabelIds)
        {
            if (labelIdsProp.ValueKind != JsonValueKind.Array)
                return ApiResults.BadRequest("labelIds must be an array.");
            newLabelIds = new List<long>();
            foreach (var el in labelIdsProp.EnumerateArray())
            {
                if (!TryReadLong(el, out var labelId) || labelId is null)
                    return ApiResults.BadRequest("labelIds entries must be numbers.");
                if (!await BelongsToProject(conn, "issue_labels", labelId.Value, projectId))
                    return ApiResults.Conflict($"labelId {labelId} does not belong to project {projectId}.");
                newLabelIds.Add(labelId.Value);
            }
        }

        var now = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();

        if (updates.Count > 0)
        {
            updates.Add("updated_at = @now");
            p.Add("now", now);
            await conn.ExecuteAsync(
                $"UPDATE issues SET {string.Join(", ", updates)} WHERE id = @id",
                p, tx);
        }

        if (hasLabelIds && newLabelIds is not null)
        {
            var current = (await conn.QueryAsync<long>(
                "SELECT issue_label_id FROM issue_label_links WHERE issue_id = @id",
                new { id }, tx)).ToHashSet();
            var desired = newLabelIds.ToHashSet();

            var toAdd = desired.Except(current).ToList();
            var toRemove = current.Except(desired).ToList();

            if (toRemove.Count > 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM issue_label_links WHERE issue_id = @id AND issue_label_id IN @ids",
                    new { id, ids = toRemove }, tx);
            }
            if (toAdd.Count > 0)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO issue_label_links (issue_id, issue_label_id, created_at, updated_at)
                      VALUES (@issueId, @labelId, @now, @now)",
                    toAdd.Select(labelId => new { issueId = id, labelId, now }),
                    tx);
            }
        }

        tx.Commit();

        var issue = await IssueRepository.LoadByIdAsync(conn, id);
        return Results.Ok(issue);
    }

    private static async Task<IResult> UpdateCustomFields(
        long id,
        JsonElement body,
        IDbConnectionFactory factory)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ApiResults.BadRequest("Request body must be a JSON object.");

        using var conn = factory.Create();
        var rawCurrent = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT custom_fields FROM issues WHERE id = @id",
            new { id });
        if (rawCurrent is null && !await IssueExists(conn, id))
            return ApiResults.NotFound($"Issue {id} does not exist.");

        var merged = MergeCustomFields(rawCurrent, body);
        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "UPDATE issues SET custom_fields = @merged, updated_at = @now WHERE id = @id",
            new { id, merged, now });

        var issue = await IssueRepository.LoadByIdAsync(conn, id);
        return Results.Ok(issue);
    }

    private static async Task<IResult> FinishIssue(long id, IDbConnectionFactory factory) =>
        await SetFinishedAt(id, factory, DateTime.UtcNow);

    private static async Task<IResult> UnfinishIssue(long id, IDbConnectionFactory factory) =>
        await SetFinishedAt(id, factory, null);

    private static async Task<IResult> SetFinishedAt(long id, IDbConnectionFactory factory, DateTime? finishedAt)
    {
        using var conn = factory.Create();
        if (!await IssueExists(conn, id))
            return ApiResults.NotFound($"Issue {id} does not exist.");

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "UPDATE issues SET finished_at = @finishedAt, updated_at = @now WHERE id = @id",
            new { id, finishedAt, now });

        var issue = await IssueRepository.LoadByIdAsync(conn, id);
        return Results.Ok(issue);
    }

    private static string MergeCustomFields(string? current, JsonElement incoming)
    {
        var result = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current);
                if (parsed is not null)
                    foreach (var kvp in parsed) result[kvp.Key] = kvp.Value;
            }
            catch (JsonException) { }
        }
        foreach (var prop in incoming.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }
        return JsonSerializer.Serialize(result);
    }

    private static List<long> ReadLabelIds(JsonElement body)
    {
        var list = new List<long>();
        if (!TryGetProperty(body, "labelIds", out var prop)) return list;
        if (prop.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in prop.EnumerateArray())
        {
            if (TryReadLong(el, out var labelId) && labelId.HasValue)
                list.Add(labelId.Value);
        }
        return list;
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.Ordinal))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement obj, string name, out string? value)
    {
        if (TryGetProperty(obj, name, out var prop))
        {
            value = prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryGetLong(JsonElement obj, string name, out long? value)
    {
        if (TryGetProperty(obj, name, out var prop))
            return TryReadLong(prop, out value);
        value = null;
        return false;
    }

    private static bool TryReadLong(JsonElement el, out long? value)
    {
        if (el.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) { value = n; return true; }
        value = null;
        return false;
    }

    private static bool TryGetDate(JsonElement obj, string name, out DateTime? value)
    {
        if (TryGetProperty(obj, name, out var prop))
            return TryReadDate(prop, out value);
        value = null;
        return false;
    }

    private static bool TryReadDate(JsonElement el, out DateTime? value)
    {
        if (el.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (el.ValueKind == JsonValueKind.String
            && DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            value = dt;
            return true;
        }
        value = null;
        return false;
    }

    private static async Task<bool> ProjectExists(IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM projects WHERE id = @id", new { id }) > 0;

    private static async Task<bool> IssueExists(IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM issues WHERE id = @id", new { id }) > 0;

    private static async Task<bool> UserExists(IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM users WHERE id = @id", new { id }) > 0;

    private static async Task<bool> BelongsToProject(IDbConnection conn, string table, long rowId, long projectId) =>
        await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(1) FROM {table} WHERE id = @rowId AND project_id = @projectId",
            new { rowId, projectId }) > 0;
}
