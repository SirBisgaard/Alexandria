using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.Tests.Library;

public record TimingRecord(string Phase, string FilePath, long ElapsedMs, long? FileSizeBytes = null);

public class TimingFfmpegHandler(IFfmpegHandler inner, ConcurrentBag<TimingRecord> timings) : IFfmpegHandler
{
    public async Task<string> GetMetadataJson(string inputPath)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetMetadataJson(inputPath);
        timings.Add(new TimingRecord("ffprobe", inputPath, sw.ElapsedMilliseconds, new FileInfo(inputPath).Length));
        return result;
    }

    public async Task<byte[]?> ExtractCoverImage(string inputPath)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.ExtractCoverImage(inputPath);
        timings.Add(new TimingRecord("ffmpeg-cover", inputPath, sw.ElapsedMilliseconds, new FileInfo(inputPath).Length));
        return result;
    }

    public Task ExtractChapterAsMp3(string inputPath, double startTime, double endTime, string outputPath) => inner.ExtractChapterAsMp3(inputPath, startTime, endTime, outputPath);
    public Task<byte[]> ExtractChapterAsMp3ToMemory(string inputPath, double startTime, double endTime) => inner.ExtractChapterAsMp3ToMemory(inputPath, startTime, endTime);
    public Task WriteMetadata(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters) => inner.WriteMetadata(inputPath, outputPath, tags, chapters);
    public Task WriteMetadataWithCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters, byte[] coverData) => inner.WriteMetadataWithCover(inputPath, outputPath, tags, chapters, coverData);
    public Task WriteMetadataRemoveCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters) => inner.WriteMetadataRemoveCover(inputPath, outputPath, tags, chapters);
}

public class TimingFileContentIndexer(IFileContentIndexer inner, ConcurrentBag<TimingRecord> timings) : IFileContentIndexer
{
    public string[] SupportedExtensions => inner.SupportedExtensions;

    public string[] GetContentFiles() => inner.GetContentFiles();

    public async Task<string> ComputeFileHash(string filePath)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.ComputeFileHash(filePath);
        timings.Add(new TimingRecord("hash", filePath, sw.ElapsedMilliseconds, new FileInfo(filePath).Length));
        return result;
    }

    public (Book Book, string? CoverFilePath) ExtractAll(string filePath, Guid bookUuid)
    {
        var sw = Stopwatch.StartNew();
        var result = inner.ExtractAll(filePath, bookUuid);
        timings.Add(new TimingRecord("extract-all", filePath, sw.ElapsedMilliseconds, new FileInfo(filePath).Length));
        return result;
    }

    public Task CreateDerivativeFiles(Book book, string sourcePath) => inner.CreateDerivativeFiles(book, sourcePath);
    public void DeleteDerivedFiles(Book book) => inner.DeleteDerivedFiles(book);
    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths) => inner.CleanupOrphanedFiles(validBookUuids, validCoverPaths);
}

public class InstrumentedBookRepository(IBookRepository inner, ConcurrentBag<TimingRecord> timings) : IBookRepository
{
    public int AddCount;
    public int UpdateCount;

    public async Task<List<Book>> GetAll()
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetAll();
        timings.Add(new TimingRecord("db-getall-books", "", sw.ElapsedMilliseconds));
        return result;
    }

    public async Task Add(Book book)
    {
        var sw = Stopwatch.StartNew();
        await inner.Add(book);
        Interlocked.Increment(ref AddCount);
        timings.Add(new TimingRecord("db-add-book", book.SourcePath, sw.ElapsedMilliseconds));
    }

    public async Task Update(Book book)
    {
        var sw = Stopwatch.StartNew();
        await inner.Update(book);
        Interlocked.Increment(ref UpdateCount);
        timings.Add(new TimingRecord("db-update-book", book.SourcePath, sw.ElapsedMilliseconds));
    }

    public Task<Book?> FindByUuid(Guid uuid) => inner.FindByUuid(uuid);
    public Task<Book?> FindByEntitlementUuid(Guid entitlementUuid) => inner.FindByEntitlementUuid(entitlementUuid);
    public Task<Book?> FindByHash(string fileHash) => inner.FindByHash(fileHash);
    public Task<Book?> FindByPath(string sourcePath) => inner.FindByPath(sourcePath);
    public Task Remove(Guid uuid) => inner.Remove(uuid);
    public Task<List<string>> GetPublishers() => inner.GetPublishers();
}

public class InstrumentedAuthorRepository(IAuthorRepository inner, ConcurrentBag<TimingRecord> timings) : IAuthorRepository
{
    public int GetOrCreateCount;
    public int SetBookAuthorsCount;

    public async Task<Guid> GetOrCreate(string name)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetOrCreate(name);
        Interlocked.Increment(ref GetOrCreateCount);
        timings.Add(new TimingRecord("db-author-getorcreate", name, sw.ElapsedMilliseconds));
        return result;
    }

    public async Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
    {
        var sw = Stopwatch.StartNew();
        await inner.SetBookAuthors(bookUuid, authorNames);
        Interlocked.Increment(ref SetBookAuthorsCount);
        timings.Add(new TimingRecord("db-author-setbookauthors", bookUuid.ToString(), sw.ElapsedMilliseconds));
    }

    public Task<List<Author>> GetAll() => inner.GetAll();
    public Task<Author?> FindByUuid(Guid authorUuid) => inner.FindByUuid(authorUuid);
    public Task<Author?> FindByName(string name) => inner.FindByName(name);
    public Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid) => inner.GetAuthorsByBookUuid(bookUuid);
    public Task RemoveOrphanedAuthors() => inner.RemoveOrphanedAuthors();
    public Task Update(Author author) => inner.Update(author);
    public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => inner.ReassignBooks(sourceUuid, targetUuid);
}

public class InstrumentedSeriesRepository(ISeriesRepository inner, ConcurrentBag<TimingRecord> timings) : ISeriesRepository
{
    public int GetOrCreateCount;
    public int SetBookSeriesCount;
    public int RemoveBookSeriesCount;

    public async Task<Guid> GetOrCreate(string name)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetOrCreate(name);
        Interlocked.Increment(ref GetOrCreateCount);
        timings.Add(new TimingRecord("db-series-getorcreate", name, sw.ElapsedMilliseconds));
        return result;
    }

    public async Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index)
    {
        var sw = Stopwatch.StartNew();
        await inner.SetBookSeries(bookUuid, seriesUuid, index);
        Interlocked.Increment(ref SetBookSeriesCount);
        timings.Add(new TimingRecord("db-series-setbookseries", bookUuid.ToString(), sw.ElapsedMilliseconds));
    }

    public async Task RemoveBookSeries(Guid bookUuid)
    {
        var sw = Stopwatch.StartNew();
        await inner.RemoveBookSeries(bookUuid);
        Interlocked.Increment(ref RemoveBookSeriesCount);
        timings.Add(new TimingRecord("db-series-removebookseries", bookUuid.ToString(), sw.ElapsedMilliseconds));
    }

    public Task<List<Series>> GetAll() => inner.GetAll();
    public Task<Series?> FindByUuid(Guid seriesUuid) => inner.FindByUuid(seriesUuid);
    public Task<Series?> GetSeriesByBookUuid(Guid bookUuid) => inner.GetSeriesByBookUuid(bookUuid);
    public Task RemoveOrphanedSeries() => inner.RemoveOrphanedSeries();
    public Task<Series?> FindByName(string name) => inner.FindByName(name);
    public Task Update(Series series) => inner.Update(series);
    public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => inner.ReassignBooks(sourceUuid, targetUuid);
}

public static class PerformanceReport
{
    public static string Generate(ConcurrentBag<TimingRecord> timings, string ebookFolder, string audiobookFolder, long totalElapsedMs, InstrumentedBookRepository? bookRepo = null, InstrumentedAuthorRepository? authorRepo = null, InstrumentedSeriesRepository? seriesRepo = null)
    {
        var sb = new StringBuilder();
        var records = timings.ToList();

        var epubCount = records.Count(r => r.Phase == "hash" && r.FilePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));
        var m4bCount = records.Count(r => r.Phase == "hash" && r.FilePath.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("=== ALEXANDRIA INDEXER PERFORMANCE REPORT ===");
        sb.AppendLine($"Ebooks:     {ebookFolder}");
        sb.AppendLine($"Audiobooks: {audiobookFolder}");
        sb.AppendLine($"Files: {epubCount + m4bCount} ({epubCount} EPUB, {m4bCount} M4B)");
        sb.AppendLine($"Total: {FormatMs(totalElapsedMs)}");
        sb.AppendLine();

        // Phase totals
        var hashTotal = records.Where(r => r.Phase == "hash").Sum(r => r.ElapsedMs);
        var ffprobeTotal = records.Where(r => r.Phase == "ffprobe").Sum(r => r.ElapsedMs);
        var ffmpegCoverTotal = records.Where(r => r.Phase == "ffmpeg-cover").Sum(r => r.ElapsedMs);
        var extractAllTotal = records.Where(r => r.Phase == "extract-all").Sum(r => r.ElapsedMs);
        var dbTotal = records.Where(r => r.Phase.StartsWith("db-")).Sum(r => r.ElapsedMs);

        sb.AppendLine("--- PHASE TOTALS (wall-clock sum across parallel threads) ---");
        AppendPhase(sb, "SHA256 Hashing", hashTotal, totalElapsedMs);
        AppendPhase(sb, "ExtractAll (meta+cover)", extractAllTotal, totalElapsedMs);
        AppendPhase(sb, "  ffprobe Metadata", ffprobeTotal, totalElapsedMs);
        AppendPhase(sb, "  ffmpeg Cover", ffmpegCoverTotal, totalElapsedMs);
        AppendPhase(sb, "DB Operations", dbTotal, totalElapsedMs);
        sb.AppendLine();

        // Slowest files by hash
        sb.AppendLine("--- SLOWEST FILES (SHA256 HASH) ---");
        sb.AppendLine($"{"File",-60} {"Size",10} {"Time",10} {"MB/s",8}");
        foreach (var r in records.Where(r => r.Phase == "hash").OrderByDescending(r => r.ElapsedMs).Take(15))
        {
            var name = TruncateFileName(r.FilePath, 58);
            var size = r.FileSizeBytes.HasValue ? FormatBytes(r.FileSizeBytes.Value) : "?";
            var throughput = r.FileSizeBytes.HasValue && r.ElapsedMs > 0 ? $"{r.FileSizeBytes.Value / 1024.0 / 1024.0 / (r.ElapsedMs / 1000.0):F1}" : "?";
            sb.AppendLine($"{name,-60} {size,10} {FormatMs(r.ElapsedMs),10} {throughput,8}");
        }
        sb.AppendLine();

        // Slowest files by ffprobe
        if (records.Any(r => r.Phase == "ffprobe"))
        {
            sb.AppendLine("--- SLOWEST FILES (FFPROBE METADATA) ---");
            sb.AppendLine($"{"File",-60} {"Size",10} {"Time",10}");
            foreach (var r in records.Where(r => r.Phase == "ffprobe").OrderByDescending(r => r.ElapsedMs).Take(15))
            {
                var name = TruncateFileName(r.FilePath, 58);
                var size = r.FileSizeBytes.HasValue ? FormatBytes(r.FileSizeBytes.Value) : "?";
                sb.AppendLine($"{name,-60} {size,10} {FormatMs(r.ElapsedMs),10}");
            }
            sb.AppendLine();
        }

        // Slowest files by ffmpeg cover
        if (records.Any(r => r.Phase == "ffmpeg-cover"))
        {
            sb.AppendLine("--- SLOWEST FILES (FFMPEG COVER EXTRACTION) ---");
            sb.AppendLine($"{"File",-60} {"Size",10} {"Time",10}");
            foreach (var r in records.Where(r => r.Phase == "ffmpeg-cover").OrderByDescending(r => r.ElapsedMs).Take(15))
            {
                var name = TruncateFileName(r.FilePath, 58);
                var size = r.FileSizeBytes.HasValue ? FormatBytes(r.FileSizeBytes.Value) : "?";
                sb.AppendLine($"{name,-60} {size,10} {FormatMs(r.ElapsedMs),10}");
            }
            sb.AppendLine();
        }

        // ExtractAll breakdown
        sb.AppendLine("--- SLOWEST FILES (EXTRACT ALL = metadata + cover) ---");
        sb.AppendLine($"{"File",-60} {"Size",10} {"Time",10}");
        foreach (var r in records.Where(r => r.Phase == "extract-all").OrderByDescending(r => r.ElapsedMs).Take(15))
        {
            var name = TruncateFileName(r.FilePath, 58);
            var size = r.FileSizeBytes.HasValue ? FormatBytes(r.FileSizeBytes.Value) : "?";
            sb.AppendLine($"{name,-60} {size,10} {FormatMs(r.ElapsedMs),10}");
        }
        sb.AppendLine();

        // DB operation counts
        if (bookRepo is not null && authorRepo is not null && seriesRepo is not null)
        {
            sb.AppendLine("--- DB OPERATION COUNTS ---");
            sb.AppendLine($"Book Add:                  {bookRepo.AddCount}");
            sb.AppendLine($"Book Update:               {bookRepo.UpdateCount}");
            sb.AppendLine($"Author GetOrCreate:         {authorRepo.GetOrCreateCount}");
            sb.AppendLine($"Author SetBookAuthors:      {authorRepo.SetBookAuthorsCount}");
            sb.AppendLine($"Series GetOrCreate:         {seriesRepo.GetOrCreateCount}");
            sb.AppendLine($"Series SetBookSeries:       {seriesRepo.SetBookSeriesCount}");
            sb.AppendLine($"Series RemoveBookSeries:    {seriesRepo.RemoveBookSeriesCount}");

            var cacheInvalidations = authorRepo.GetOrCreateCount + authorRepo.SetBookAuthorsCount + seriesRepo.GetOrCreateCount + seriesRepo.SetBookSeriesCount + seriesRepo.RemoveBookSeriesCount + bookRepo.AddCount + bookRepo.UpdateCount;
            sb.AppendLine();
            sb.AppendLine($"Estimated cache invalidations (if caching repos): {cacheInvalidations}");
            sb.AppendLine($"  Each InvalidateAuthors also clears book cache (cascade)");
            sb.AppendLine($"  Each InvalidateSeries also clears book cache (cascade)");

            var bookCacheClears = cacheInvalidations;
            sb.AppendLine($"  Total book cache clears: ~{bookCacheClears}");
            sb.AppendLine();

            // DB timing breakdown
            var dbRecords = records.Where(r => r.Phase.StartsWith("db-")).ToList();
            foreach (var group in dbRecords.GroupBy(r => r.Phase).OrderByDescending(g => g.Sum(r => r.ElapsedMs)))
            {
                sb.AppendLine($"  {group.Key,-35} {group.Count(),5} calls  {FormatMs(group.Sum(r => r.ElapsedMs)),10} total  {group.Average(r => r.ElapsedMs):F1}ms avg");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendPhase(StringBuilder sb, string name, long phaseMs, long totalMs)
    {
        var pct = totalMs > 0 ? (double)phaseMs / totalMs * 100 : 0;
        sb.AppendLine($"  {name,-30} {FormatMs(phaseMs),10}  ({pct:F0}% of wall-clock)");
    }

    private static string FormatMs(long ms)
    {
        if (ms < 1000)
            return $"{ms}ms";
        if (ms < 60_000)
            return $"{ms / 1000.0:F1}s";
        return $"{ms / 60_000}m {ms % 60_000 / 1000}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private static string TruncateFileName(string path, int maxLen)
    {
        var name = Path.GetFileName(path);
        if (name.Length <= maxLen)
            return name;
        return name[..(maxLen - 3)] + "...";
    }
}
