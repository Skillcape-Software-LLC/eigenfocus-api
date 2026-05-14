namespace EigenfocusApi.Models;

public sealed class IssueStatus
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ProjectId { get; set; }
    public bool Initial { get; set; }
    public bool Final { get; set; }
}
