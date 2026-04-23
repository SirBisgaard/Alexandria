using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Library.Services;

public class FileManagementService(
    IBookRepository bookRepository,
    ISettingsService settingsService,
    IEnumerable<IFileRenamer> renamers,
    IProgressService progressService,
    IDatabasePersistence databasePersistence,
    ILogger<FileManagementService> logger) : IFileManagementService
{
    public async Task<FileManagementPlan> ComputePlan()
    {
        var settings = await settingsService.Get();
        var template = settings.FileRenameTemplate;

        var books = await bookRepository.GetAll();
        var orderedBooks = books.OrderBy(b => b.BookUuid).ToList();

        var operations = new List<FileManagementOperation>();
        var skipped = new List<FileManagementSkipped>();
        var claimed = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var conflicts = new List<FileManagementConflict>();

        foreach (var book in orderedBooks)
        {
            var renamer = TryGetRenamer(book.File.SourcePath);
            if (renamer is null)
            {
                logger.LogWarning("No renamer registered for {Path}; skipping {Uuid}", book.File.SourcePath, book.BookUuid);
                continue;
            }

            var renderedBase = FileNameTemplate.Render(book, template);

            if (string.IsNullOrWhiteSpace(renderedBase))
            {
                logger.LogWarning("Template rendered empty base name for {Uuid}; skipping", book.BookUuid);
                continue;
            }

            var step = renamer.PlanRename(book, renderedBase);

            if (string.Equals(step.OldSourcePath, step.NewSourcePath, StringComparison.Ordinal))
            {
                skipped.Add(new FileManagementSkipped
                {
                    BookUuid = book.BookUuid,
                    Title = book.Title,
                    CurrentPath = book.File.SourcePath
                });
                continue;
            }

            if (claimed.TryGetValue(step.NewSourcePath, out var existingBookUuid))
            {
                conflicts.Add(new FileManagementConflict
                {
                    Kind = FileManagementConflictKind.Duplicate,
                    Path = step.NewSourcePath,
                    BookUuids = [existingBookUuid, book.BookUuid]
                });
                continue;
            }

            if (PhysicalExists(step.NewSourcePath))
            {
                conflicts.Add(new FileManagementConflict
                {
                    Kind = FileManagementConflictKind.ExistingFile,
                    Path = step.NewSourcePath,
                    BookUuids = [book.BookUuid]
                });
                continue;
            }

            claimed[step.NewSourcePath] = book.BookUuid;
            operations.Add(new FileManagementOperation
            {
                BookUuid = book.BookUuid,
                Title = book.Title,
                MediaType = book.File.MediaType,
                CurrentPath = step.OldSourcePath,
                NewPath = step.NewSourcePath,
                NewBaseName = renderedBase,
                ChapterRenames = step.ChapterRenames.Select(c => new FileManagementChapterRename
                {
                    ChapterIndex = c.ChapterIndex,
                    OldFileName = c.OldFileName,
                    NewFileName = c.NewFileName
                }).ToList()
            });
        }

        return new FileManagementPlan
        {
            Template = template,
            Operations = operations,
            Skipped = skipped,
            Conflicts = conflicts
        };
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        var plan = await ComputePlan();
        if (plan.HasConflicts)
            throw new FileManagementConflictException(plan.Conflicts);

        var renamed = 0;
        var failed = new List<FileManagementFailure>();
        var ordinal = 0;
        var total = plan.Operations.Count;

        foreach (var operation in plan.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ordinal++;
            progressService.Report("Renaming", ordinal, total);

            try
            {
                await ExecuteOne(operation);
                renamed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "File rename failed for {Uuid} ({Title})", operation.BookUuid, operation.Title);
                failed.Add(new FileManagementFailure { BookUuid = operation.BookUuid, Error = ex.Message });
            }
        }

        await databasePersistence.Flush();
        progressService.ReportDone(new FileManagementCompletion { Renamed = renamed, Skipped = plan.Skipped.Count, Failed = failed });
    }

    private async Task ExecuteOne(FileManagementOperation operation)
    {
        var renamer = TryGetRenamer(operation.CurrentPath)
            ?? throw new InvalidOperationException($"No renamer registered for {operation.CurrentPath}.");

        var book = await bookRepository.FindByUuid(operation.BookUuid)
            ?? throw new InvalidOperationException($"Book {operation.BookUuid} no longer exists.");

        if (!string.Equals(book.File.SourcePath, operation.CurrentPath, StringComparison.Ordinal))
            throw new InvalidOperationException("Source path changed between plan and commit.");

        if (PhysicalExists(operation.NewPath))
            throw new InvalidOperationException($"Target path exists: {operation.NewPath}");

        var step = renamer.PlanRename(book, operation.NewBaseName);
        var subProgress = new DelegateProgress<ProgressTask>(t => progressService.ReportSubTask(t.Title, t.Count, t.Total));
        renamer.Execute(step, subProgress);

        try
        {
            book.File.SourcePath = step.NewSourcePath;
            foreach (var chapterRename in step.ChapterRenames)
            {
                var chapter = book.Chapters.FirstOrDefault(c => c.ChapterIndex == chapterRename.ChapterIndex);
                if (chapter is not null)
                    chapter.FileName = chapterRename.NewFileName;
            }
            await bookRepository.Update(book);
        }
        catch
        {
            try
            {
                renamer.Execute(step.Invert());
            }
            catch (Exception revertEx)
            {
                logger.LogError(revertEx, "Rollback of filesystem rename failed for {Uuid}. Manual intervention required.", operation.BookUuid);
            }
            throw;
        }
    }

    private IFileRenamer? TryGetRenamer(string sourcePath)
    {
        var extension = GetVirtualExtension(sourcePath);
        return renamers.FirstOrDefault(r => r.SupportedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetVirtualExtension(string sourcePath)
    {
        const string mp3Suffix = ".mp3audiobook";
        if (sourcePath.EndsWith(mp3Suffix, StringComparison.OrdinalIgnoreCase))
            return mp3Suffix;
        return Path.GetExtension(sourcePath);
    }

    private sealed class DelegateProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private static bool PhysicalExists(string virtualPath)
    {
        const string mp3Suffix = ".mp3audiobook";
        if (virtualPath.EndsWith(mp3Suffix, StringComparison.OrdinalIgnoreCase))
        {
            var folder = virtualPath[..^mp3Suffix.Length];
            return Directory.Exists(folder) || File.Exists(folder);
        }
        return File.Exists(virtualPath) || Directory.Exists(virtualPath);
    }
}
