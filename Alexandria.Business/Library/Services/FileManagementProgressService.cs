using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class FileManagementProgressService : IFileManagementProgressService
{
    private IProgress<FileManagementReport>? progress;
    private IProgress<FileManagementCompletion>? completion;

    public void Subscribe(IProgress<FileManagementReport> onProgress, IProgress<FileManagementCompletion> onCompletion)
    {
        progress = onProgress;
        completion = onCompletion;
    }

    public void ReportRenaming(string bookTitle, int ordinal, int total)
    {
        progress?.Report(new FileManagementReport { Title = bookTitle, Ordinal = ordinal, Total = total });
    }

    public void ReportDone(int renamed, int skipped, IReadOnlyList<FileManagementFailure> failed)
    {
        completion?.Report(new FileManagementCompletion { Renamed = renamed, Skipped = skipped, Failed = failed });
    }
}
