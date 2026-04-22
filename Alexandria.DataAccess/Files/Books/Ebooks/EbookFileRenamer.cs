using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.DataAccess.Files.Books.Ebooks;

public class EbookFileRenamer : IFileRenamer
{
    public string[] SupportedExtensions => [".epub"];

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

    public void Execute(FileRenameStep step)
    {
        if (step.IsNoOp)
            return;
        FileSystemSafeMove.File(step.OldSourcePath, step.NewSourcePath);
    }
}
