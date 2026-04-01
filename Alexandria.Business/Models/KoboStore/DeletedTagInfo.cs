namespace Alexandria.Business.Models.KoboStore;

public record DeletedTagInfo
{
    public required string Id { get; set; }
    public required string LastModified { get; set; }
}