using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentIndexer
{
    string[] SupportedExtensions { get; }
    string[] GetContentFiles();
    Task<string> ComputeFileHash(string filePath);
    (Book Book, string? CoverFilePath) ExtractAll(string filePath);
    Task CreateDerivativeFiles(Book book, string sourcePath);
    void DeleteDerivedFiles(Book book);
    void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths);
}
