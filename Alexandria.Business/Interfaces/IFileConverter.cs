using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface IFileConverter
{
    /// <summary>
    /// Runs any pre-indexing source-file conversion this converter is responsible for.
    /// Invoked from <c>IndexerService.Index</c> before file discovery, so any converted
    /// output is picked up by the regular indexers on the same run. Implementations must
    /// be idempotent — a no-op when nothing needs converting is expected.
    /// </summary>
    /// <param name="existingBooks">
    ///     Snapshot of books currently in the database. Converters that replace a source file
    ///     with a different format should use this to locate and clean up derivative files
    ///     keyed to the previous book record (e.g. cached chapter MP3s under ChaptersFolder).
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>The count of books that failed conversion.</returns>
    Task<int> Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken);
}
