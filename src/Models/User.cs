namespace EigenfocusApi.Models;

public sealed class User
{
    public long Id { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string? Locale { get; set; }
    public string? Timezone { get; set; }
    public string? Role { get; set; }
    public DateTime CreatedAt { get; set; }
}
