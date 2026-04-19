using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface IFileConverter
{
    /// <summary>
    /// Runs any pre-indexing source-file conversion this converter is responsible for.
    /// Invoked from <c>IndexerService.Index</c> before file discovery, so any converted
    /// output is picked up by the regular indexers on the same run. Implementations must
    /// be idempotent — a no-op when nothing needs converting is expected.
    /// Per-book failures are handled inside the converter (typically by quarantining the
    /// source file) so a single bad book never aborts the wider indexing run. Cancellation
    /// does propagate.
    /// </summary>
    /// <param name="existingBooks">
    ///     Snapshot of books currently in the database. Converters that replace a source file
    ///     with a different format should use this to locate and clean up derivative files
    ///     keyed to the previous book record (e.g. cached chapter MP3s under ChaptersFolder).
    /// </param>
    /// <param name="cancellationToken"></param>
    Task Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken);
}
