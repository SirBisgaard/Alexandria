using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.DataAccess.Files.Books.Comics;

public class ComicFileRenamer : IFileRenamer
{
    public bool CanHandle(string path) => path.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase);

    public FileRenameStep PlanRename(Book book, string newBaseName)
    {
        var oldPath = book.File.SourcePath;
        var directory = Path.GetDirectoryName(oldPath) ?? string.Empty;
        return new FileRenameStep
        {
            OldSourcePath = oldPath,
            NewSourcePath = Path.Combine(directory, newBaseName + Path.GetExtension(oldPath))
        };
    }

    public void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null)
    {
        if (step.IsNoOperation)
            return;
        FileSystemSafeMove.File(step.OldSourcePath, step.NewSourcePath);
    }
}
