namespace Alexandria.Business.Interfaces;

public interface ILibraryVersionRepository
{
    /// <summary>
    /// Cheap fingerprint of all library data (books, authors, series, reading states, collections,
    /// activity events). Any write to those tables yields a different token; identical tokens
    /// guarantee unchanged data. Used as a weak ETag for conditional GET requests.
    /// </summary>
    Task<string> GetVersionToken();
}
