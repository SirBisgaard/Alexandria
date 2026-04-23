using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class Mp3FolderFileRenamer : IFileRenamer
{
    public bool CanHandle(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.mp3").Any();

    public FileRenameStep PlanRename(Book book, string newBaseName)
    {
        var oldFolder = book.File.SourcePath;
        var parent = Path.GetDirectoryName(oldFolder) ?? string.Empty;
        var newFolder = Path.Combine(parent, newBaseName);

        var chapters = book.Chapters
            .Where(c => !string.IsNullOrEmpty(c.FileName))
            .OrderBy(c => c.ChapterIndex)
            .ToList();

        var renames = new List<ChapterFileRename>(chapters.Count);
        if (chapters.Count > 0)
        {
            var width = Math.Max(2, chapters.Count.ToString().Length);
            var safeTitle = FileNameTemplate.SanitizeFilename(book.Title);
            for (var i = 0; i < chapters.Count; i++)
            {
                var chapter = chapters[i];
                var ordinal = (i + 1).ToString().PadLeft(width, '0');
                var newName = string.IsNullOrEmpty(safeTitle)
                    ? $"{ordinal}.mp3"
                    : $"{ordinal} - {safeTitle}.mp3";
                renames.Add(new ChapterFileRename
                {
                    ChapterIndex = chapter.ChapterIndex,
                    OldFileName = chapter.FileName!,
                    NewFileName = newName
                });
            }
        }

        return new FileRenameStep
        {
            OldSourcePath = oldFolder,
            NewSourcePath = newFolder,
            ChapterRenames = renames
        };
    }

    public void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null)
    {
        if (step.IsNoOperation)
            return;

        var oldFolder = step.OldSourcePath;
        var newFolder = step.NewSourcePath;
        var folderMoved = false;

        if (!string.Equals(oldFolder, newFolder, StringComparison.Ordinal))
        {
            FileSystemSafeMove.Directory(oldFolder, newFolder);
            folderMoved = true;
        }

        var completed = new List<(string Source, string Destination)>();
        try
        {
            var totalChapters = step.ChapterRenames.Count;
            var chapterIndex = 0;
            foreach (var rename in step.ChapterRenames)
            {
                chapterIndex++;
                if (string.Equals(rename.OldFileName, rename.NewFileName, StringComparison.Ordinal))
                    continue;
                var source = Path.Combine(newFolder, rename.OldFileName);
                var destination = Path.Combine(newFolder, rename.NewFileName);
                FileSystemSafeMove.File(source, destination);
                completed.Add((source, destination));
                subProgress?.Report(new ProgressTask { Title = "Renaming chapters", Count = chapterIndex, Total = totalChapters });
            }
        }
        catch
        {
            for (var i = completed.Count - 1; i >= 0; i--)
            {
                var (source, destination) = completed[i];
                try
                {
                    FileSystemSafeMove.File(destination, source);
                }
                catch
                {
                }
            }
            if (folderMoved)
            {
                try
                {
                    FileSystemSafeMove.Directory(newFolder, oldFolder);
                }
                catch
                {
                }
            }
            throw;
        }
    }
}
