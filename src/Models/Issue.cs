using System.Text.Json;

namespace EigenfocusApi.Models;

public sealed class Issue
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
    public Dictionary<string, JsonElement> CustomFields { get; set; } = new();
    public List<IssueLabel> Labels { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
