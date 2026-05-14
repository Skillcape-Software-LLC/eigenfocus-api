namespace EigenfocusApi.Models;

public sealed class Project
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? ArchivedAt { get; set; }
    public bool TimeTrackingEnabled { get; set; }
    public bool OpenToAllUsers { get; set; }
    public long? GroupId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
