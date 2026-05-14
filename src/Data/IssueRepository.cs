using System.Data;
using System.Text.Json;
using Dapper;
using EigenfocusApi.Models;

namespace EigenfocusApi.Data;

/// <summary>
/// Loads issues with hydrated <c>custom_fields</c> and labels in a single round trip per call.
/// Centralized so every endpoint that returns an issue produces an identical shape.
/// </summary>
public static class IssueRepository
{
    private const string IssueColumns =
        "id, title, description, project_id, status_id, type_id, assignee_id, parent_id, " +
        "rank, due_date, start_date, end_date, archived_at, finished_at, comments_count, " +
        "custom_fields, created_at, updated_at";

    /// <summary>
    /// Loads a single issue by id with its labels and parsed <c>custom_fields</c> hydrated.
    /// </summary>
    /// <returns>The hydrated issue, or <c>null</c> if no row matches.</returns>
    public static async Task<Issue?> LoadByIdAsync(IDbConnection conn, long id, IDbTransaction? tx = null)
    {
        var row = await conn.QuerySingleOrDefaultAsync<IssueRow>(
            $"SELECT {IssueColumns} FROM issues WHERE id = @id",
            new { id }, tx);
        if (row is null) return null;

        var labels = (await LoadLabelsAsync(conn, new[] { id }, tx)).GetValueOrDefault(id, new List<IssueLabel>());
        return Hydrate(row, labels);
    }

    /// <summary>
    /// Loads every issue matching the supplied <paramref name="whereClause"/> with labels hydrated in a single follow-up query.
    /// </summary>
    /// <param name="whereClause">SQL fragment appended after <c>SELECT ... FROM issues</c> (e.g. <c>"WHERE project_id = @projectId"</c>). May be empty.</param>
    /// <param name="parameters">Dapper parameter object matching the placeholders in <paramref name="whereClause"/>.</param>
    public static async Task<IReadOnlyList<Issue>> LoadManyAsync(
        IDbConnection conn,
        string whereClause,
        object parameters,
        IDbTransaction? tx = null)
    {
        var rows = (await conn.QueryAsync<IssueRow>(
            $"SELECT {IssueColumns} FROM issues {whereClause}",
            parameters, tx)).ToList();
        if (rows.Count == 0) return Array.Empty<Issue>();

        var labelsByIssue = await LoadLabelsAsync(conn, rows.Select(r => r.Id), tx);
        return rows.Select(r => Hydrate(r, labelsByIssue.GetValueOrDefault(r.Id, new List<IssueLabel>()))).ToList();
    }

    /// <summary>
    /// Loads the labels for the given issue ids, grouped by issue id. Issues with no labels are absent from the map.
    /// </summary>
    public static async Task<Dictionary<long, List<IssueLabel>>> LoadLabelsAsync(
        IDbConnection conn,
        IEnumerable<long> issueIds,
        IDbTransaction? tx = null)
    {
        var ids = issueIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, List<IssueLabel>>();

        var rows = await conn.QueryAsync<LabelLinkRow>(
            @"SELECT l.id, l.title, l.project_id, l.hex_color, ill.issue_id
              FROM issue_label_links ill
              INNER JOIN issue_labels l ON l.id = ill.issue_label_id
              WHERE ill.issue_id IN @ids",
            new { ids }, tx);

        var map = new Dictionary<long, List<IssueLabel>>();
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.IssueId, out var list))
            {
                list = new List<IssueLabel>();
                map[r.IssueId] = list;
            }
            list.Add(new IssueLabel
            {
                Id = r.Id,
                Title = r.Title,
                ProjectId = r.ProjectId,
                HexColor = r.HexColor,
            });
        }
        return map;
    }

    private static Issue Hydrate(IssueRow row, List<IssueLabel> labels) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Description = row.Description,
        ProjectId = row.ProjectId,
        StatusId = row.StatusId,
        TypeId = row.TypeId,
        AssigneeId = row.AssigneeId,
        ParentId = row.ParentId,
        Rank = row.Rank,
        DueDate = row.DueDate,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
        ArchivedAt = row.ArchivedAt,
        FinishedAt = row.FinishedAt,
        CommentsCount = row.CommentsCount,
        CustomFields = ParseCustomFields(row.CustomFields),
        Labels = labels,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };

    private static Dictionary<string, JsonElement> ParseCustomFields(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, JsonElement>();
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            return parsed ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private sealed class IssueRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long ProjectId { get; set; }
        public long StatusId { get; set; }
        public long TypeId { get; set; }
        public long? AssigneeId { get; set; }
        public long? ParentId { get; set; }
        public long Rank { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int CommentsCount { get; set; }
        public string? CustomFields { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class LabelLinkRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public long ProjectId { get; set; }
        public string? HexColor { get; set; }
        public long IssueId { get; set; }
    }
}
