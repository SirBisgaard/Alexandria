namespace Alexandria.Business.Models.Settings;

public class AlexandriaSettings
{
    public required string IndexFile { get; init; }
    public required string BooksFolder { get; init; }
    public required string CoversFolder { get; init; }
    public required string KepubsFolder { get; init; }
}