namespace EigenfocusApi.Models;

public sealed class IssueLabel
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public long ProjectId { get; set; }
    public string? HexColor { get; set; }
}
