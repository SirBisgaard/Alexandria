namespace Alexandria.Business.Integrations.Models;

public class AudioBookShelfIntegration
{
    public Guid UserUuid { get; set; }
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
