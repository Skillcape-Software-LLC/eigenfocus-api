using Dapper;
using EigenfocusApi.Data;
using EigenfocusApi.Models;

namespace EigenfocusApi.Endpoints;

/// <summary>
/// Registers the read-only <c>GET</c> endpoints (projects, statuses, types, labels, users, issues, comments).
/// </summary>
public static class ReadEndpoints
{
    /// <summary>
    /// Maps every read endpoint under the <c>/api</c> route group on the supplied builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/projects", GetProjects);
        api.MapGet("/projects/{id:long}", GetProject);
        api.MapGet("/projects/{id:long}/statuses", GetProjectStatuses);
        api.MapGet("/projects/{id:long}/types", GetProjectTypes);
        api.MapGet("/projects/{id:long}/labels", GetProjectLabels);

        api.MapGet("/users", GetUsers);
        api.MapGet("/users/{id:long}", GetUser);

        api.MapGet("/projects/{projectId:long}/issues", GetProjectIssues);
        api.MapGet("/issues/{id:long}", GetIssue);
        api.MapGet("/issues/{issueId:long}/comments", GetIssueComments);
    }

    private static async Task<IResult> GetProjects(IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        var projects = await conn.QueryAsync<Project>(
            @"SELECT id, name, archived_at, time_tracking_enabled, open_to_all_users,
                     group_id, created_at, updated_at
              FROM projects
              WHERE archived_at IS NULL
              ORDER BY name");
        return Results.Ok(projects);
    }

    private static async Task<IResult> GetProject(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        var project = await conn.QuerySingleOrDefaultAsync<Project>(
            @"SELECT id, name, archived_at, time_tracking_enabled, open_to_all_users,
                     group_id, created_at, updated_at
              FROM projects WHERE id = @id",
            new { id });
        return project is null
            ? ApiResults.NotFound($"Project {id} does not exist.")
            : Results.Ok(project);
    }

    private static async Task<IResult> GetProjectStatuses(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        if (!await ProjectExists(conn, id))
            return ApiResults.NotFound($"Project {id} does not exist.");
        var rows = await conn.QueryAsync<IssueStatus>(
            @"SELECT id, name, project_id, initial, final
              FROM issue_statuses WHERE project_id = @id ORDER BY id",
            new { id });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetProjectTypes(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        if (!await ProjectExists(conn, id))
            return ApiResults.NotFound($"Project {id} does not exist.");
        var rows = await conn.QueryAsync<IssueType>(
            @"SELECT id, name, project_id, ""default"", hex_color
              FROM issue_types WHERE project_id = @id ORDER BY name",
            new { id });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetProjectLabels(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        if (!await ProjectExists(conn, id))
            return ApiResults.NotFound($"Project {id} does not exist.");
        var rows = await conn.QueryAsync<IssueLabel>(
            @"SELECT id, title, project_id, hex_color
              FROM issue_labels WHERE project_id = @id ORDER BY title",
            new { id });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetUsers(IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        var rows = await conn.QueryAsync<User>(
            @"SELECT id, alias, locale, timezone, role, created_at
              FROM users ORDER BY alias");
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetUser(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        var user = await conn.QuerySingleOrDefaultAsync<User>(
            @"SELECT id, alias, locale, timezone, role, created_at
              FROM users WHERE id = @id",
            new { id });
        return user is null
            ? ApiResults.NotFound($"User {id} does not exist.")
            : Results.Ok(user);
    }

    private static async Task<IResult> GetProjectIssues(
        long projectId,
        IDbConnectionFactory factory,
        long? statusId,
        long? typeId,
        long? assigneeId,
        string? archivingStatus,
        long? parentId)
    {
        using var conn = factory.Create();
        if (!await ProjectExists(conn, projectId))
            return ApiResults.NotFound($"Project {projectId} does not exist.");

        var (where, parameters) = BuildIssueFilter(projectId, statusId, typeId, assigneeId, archivingStatus, parentId);
        var issues = await IssueRepository.LoadManyAsync(conn, where + " ORDER BY rank", parameters);
        return Results.Ok(issues);
    }

    private static async Task<IResult> GetIssue(long id, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        var issue = await IssueRepository.LoadByIdAsync(conn, id);
        return issue is null
            ? ApiResults.NotFound($"Issue {id} does not exist.")
            : Results.Ok(issue);
    }

    private static async Task<IResult> GetIssueComments(long issueId, IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        if (!await IssueExists(conn, issueId))
            return ApiResults.NotFound($"Issue {issueId} does not exist.");
        var rows = await conn.QueryAsync<IssueComment>(
            @"SELECT id, content, issue_id, author_id, created_at, updated_at
              FROM issue_comments WHERE issue_id = @issueId ORDER BY created_at",
            new { issueId });
        return Results.Ok(rows);
    }

    private static (string Where, DynamicParameters Parameters) BuildIssueFilter(
        long projectId,
        long? statusId,
        long? typeId,
        long? assigneeId,
        string? archivingStatus,
        long? parentId)
    {
        var clauses = new List<string> { "project_id = @projectId" };
        var p = new DynamicParameters();
        p.Add("projectId", projectId);

        switch ((archivingStatus ?? "active").ToLowerInvariant())
        {
            case "active":
                clauses.Add("archived_at IS NULL");
                clauses.Add("finished_at IS NULL");
                break;
            case "archived":
                clauses.Add("archived_at IS NOT NULL");
                break;
            case "finished":
                clauses.Add("finished_at IS NOT NULL");
                break;
            case "all":
                break;
            default:
                clauses.Add("archived_at IS NULL");
                clauses.Add("finished_at IS NULL");
                break;
        }

        if (statusId.HasValue)
        {
            clauses.Add("status_id = @statusId");
            p.Add("statusId", statusId.Value);
        }
        if (typeId.HasValue)
        {
            clauses.Add("type_id = @typeId");
            p.Add("typeId", typeId.Value);
        }
        if (assigneeId.HasValue)
        {
            clauses.Add("assignee_id = @assigneeId");
            p.Add("assigneeId", assigneeId.Value);
        }
        if (parentId.HasValue)
        {
            if (parentId.Value == 0)
            {
                clauses.Add("parent_id IS NULL");
            }
            else
            {
                clauses.Add("parent_id = @parentId");
                p.Add("parentId", parentId.Value);
            }
        }

        return ("WHERE " + string.Join(" AND ", clauses), p);
    }

    private static async Task<bool> ProjectExists(System.Data.IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM projects WHERE id = @id", new { id }) > 0;

    private static async Task<bool> IssueExists(System.Data.IDbConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM issues WHERE id = @id", new { id }) > 0;
}
