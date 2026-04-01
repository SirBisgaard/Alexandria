namespace Alexandria.Business.Models.KoboStore;

public record TagItem
{
    public required string RevisionId { get; set; }
    public string Type { get; set; } = "ProductRevisionTagItem";
}