namespace Alexandria.Business.Models.KoboStore;

public record DeletedTag
{
    public required DeletedTagInfo Tag { get; set; }
}