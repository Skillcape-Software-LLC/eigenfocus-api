namespace EigenfocusApi.Models;

public sealed class IssueType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ProjectId { get; set; }
    public bool Default { get; set; }
    public string? HexColor { get; set; }
}
