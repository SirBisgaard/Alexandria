using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class Mp3FolderFileRenamer : IFileRenamer
{
    private const string VirtualSuffix = ".mp3audiobook";

    public string[] SupportedExtensions => [VirtualSuffix];

    public FileRenameStep PlanRename(Book book, string newBaseName)
    {
        var oldSourcePath = book.File.SourcePath;
        var oldFolder = StripSuffix(oldSourcePath);
        var parent = Path.GetDirectoryName(oldFolder) ?? string.Empty;
        var newFolder = Path.Combine(parent, newBaseName);
        var newSourcePath = newFolder + VirtualSuffix;

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
            OldSourcePath = oldSourcePath,
            NewSourcePath = newSourcePath,
            ChapterRenames = renames
        };
    }

    public void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null)
    {
        if (step.IsNoOperation)
            return;

        var oldFolder = StripSuffix(step.OldSourcePath);
        var newFolder = StripSuffix(step.NewSourcePath);
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

    private static string StripSuffix(string sourcePath)
    {
        if (!sourcePath.EndsWith(VirtualSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected path ending with '{VirtualSuffix}': {sourcePath}");
        return sourcePath[..^VirtualSuffix.Length];
    }
}
