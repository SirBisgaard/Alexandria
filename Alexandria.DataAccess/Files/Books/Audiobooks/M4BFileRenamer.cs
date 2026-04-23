using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class M4BFileRenamer : IFileRenamer
{
    public bool CanHandle(string path) => path.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase);

    public FileRenameStep PlanRename(Book book, string newBaseName)
    {
        var oldPath = book.File.SourcePath;
        var directory = Path.GetDirectoryName(oldPath) ?? string.Empty;
        var extension = Path.GetExtension(oldPath);
        var newPath = Path.Combine(directory, newBaseName + extension);
        return new FileRenameStep
        {
            OldSourcePath = oldPath,
            NewSourcePath = newPath
        };
    }

    public void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null)
    {
        if (step.IsNoOperation)
            return;
        FileSystemSafeMove.File(step.OldSourcePath, step.NewSourcePath);
    }
}
