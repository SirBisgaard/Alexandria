namespace Alexandria.Business.Models.Settings;

public record AlexandriaSettings
{
    public required string Database { get; init; }
    public required string BooksFolder { get; init; }
    public required string CoversFolder { get; init; }
    public required string KepubsFolder { get; init; }
}