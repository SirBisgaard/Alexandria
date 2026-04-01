namespace Alexandria.Business.Models.KoboStore;

public record BookEntitlement
{
    public string Accessibility { get; set; } = "Full";
    public required ActivePeriod ActivePeriod { get; set; }
    public required string Created { get; set; }
    public required string CrossRevisionId { get; set; }
    public required string Id { get; set; }
    public bool IsRemoved { get; set; }
    public bool IsHiddenFromArchive { get; set; }
    public bool IsLocked { get; set; }
    public required string LastModified { get; set; }
    public string OriginCategory { get; set; } = "Imported";
    public required string RevisionId { get; set; }
    public string Status { get; set; } = "Active";
}