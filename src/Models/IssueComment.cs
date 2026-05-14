namespace EigenfocusApi.Models;

public sealed class IssueComment
{
    public long Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public long IssueId { get; set; }
    public long AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
