namespace Alexandria.Business.Models.KoboStore;

public record Tag
{
    public required string Created { get; set; }
    public required string Id { get; set; }
    public List<TagItem>? Items { get; set; }
    public required string LastModified { get; set; }
    public required string Name { get; set; }
    public string Type { get; set; } = "UserTag";
}