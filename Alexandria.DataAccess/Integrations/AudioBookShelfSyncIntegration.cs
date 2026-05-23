using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alexandria.Business.Integrations.Models;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Integrations;

public class AudioBookShelfSyncIntegration(HttpClient httpClient, IAudioBookShelfSyncIntegrationRepository repository, IBookRepository bookRepository, IReadingStateRepository readingStateRepository, ILogger<AudioBookShelfSyncIntegration> logger) : IAudioBookShelfSyncIntegration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Drives one tick of the two-way audiobook reconciliation: for every user with an enabled
    /// AudioBookShelf integration, fetches the user's ABS library items + media progress, matches
    /// each item by filename (folder basename for MP3 folders, .m4b filename for M4B audiobooks)
    /// to a local audiobook, and pulls or pushes per match based on which side is newer.
    ///
    /// <para>Echo suppression is load-bearing on event ordering, not on book-level bookkeeping.
    /// After every reconciled book in a user's batch is processed, the integration stamps
    /// <c>LastSyncedAt = UtcNow</c>. On the next tick that timestamp becomes the cutoff: any match
    /// whose <c>LastUpdatedAt = max(local.ReadingProgress.ModifiedAt, abs.lastUpdate)</c> is not
    /// strictly greater than the cutoff is filtered out. Because ABS's <c>lastUpdate</c> from a
    /// just-completed push, and the local <c>ModifiedAt</c> from a just-completed pull, both land
    /// before <c>UpdateLastSynced(UtcNow)</c> runs, neither replays in the following tick.</para>
    ///
    /// <para>This means moving the <c>UpdateLastSynced</c> call earlier in <c>SyncOne</c> would
    /// silently turn the loop into a self-replay loop — the local Upsert (which stamps
    /// <c>ModifiedAt</c> to <c>UtcNow</c>) would land after the new cutoff, and the next tick would
    /// push that synthetic change back to ABS. Don't reorder without rethinking the design.</para>
    /// </summary>
    public async Task Sync(CancellationToken cancellationToken)
    {
        var integrations = await repository.GetAllEnabled();
        if (integrations.Count == 0)
        {
            logger.LogDebug("AudioBookShelf: no enabled integrations, nothing to sync");
            return;
        }

        logger.LogInformation("AudioBookShelf: starting sync ({Count} {Users})", integrations.Count, integrations.Count == 1 ? "user" : "users");

        var localAudiobooks = await LoadLocalAudiobooks();

        foreach (var integration in integrations)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await SyncOne(integration, localAudiobooks, cancellationToken);
        }
    }

    public async Task SyncForUser(Guid userUuid, CancellationToken cancellationToken)
    {
        var integration = await repository.GetByUserUuid(userUuid);
        if (integration is null || !integration.Enabled)
        {
            logger.LogInformation("AudioBookShelf: manual sync for {UserTag} skipped — no enabled integration", userUuid.ToString()[..8]);
            return;
        }

        logger.LogInformation("AudioBookShelf: manual sync requested for user {UserTag}", userUuid.ToString()[..8]);
        var localAudiobooks = await LoadLocalAudiobooks();
        await SyncOne(integration, localAudiobooks, cancellationToken, ignoreCutoff: true);
    }

    private async Task<List<Book>> LoadLocalAudiobooks()
    {
        var localAudiobooks = (await bookRepository.GetAll())
            .Where(b => b.File.MediaType == MediaType.Audiobook && !b.IsArchived)
            .ToList();

        // BookRepository.GetAll() does not hydrate ReadingProgress, so the timestamp comparison would
        // always see local as MinValue. Fetch it per audiobook so localTimestamp reflects reality.
        foreach (var book in localAudiobooks)
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.BookUuid);

        return localAudiobooks;
    }

    private async Task SyncOne(AudioBookShelfIntegration integration, List<Book> localAudiobooks, CancellationToken cancellationToken, bool ignoreCutoff = false)
    {
        var userTag = integration.UserUuid.ToString()[..8];
        try
        {
            var absItems = await FetchAllAbsItems(integration.ServerUrl, integration.ApiToken, cancellationToken);
            var absProgressByItemId = await FetchMediaProgress(integration.ServerUrl, integration.ApiToken, cancellationToken);
            var matches = MatchByFilename(localAudiobooks, absItems, absProgressByItemId);

            var cutoff = ignoreCutoff ? null : integration.LastSyncedAt;
            var changedMatches = cutoff is null
                ? matches
                : matches.Where(m => m.LastUpdatedAt > cutoff.Value).ToList();

            logger.LogInformation("AudioBookShelf: user {UserTag} — {Local} local audiobook(s), {AbsItems} ABS item(s), matched {Matched}, {Changed} to reconcile{Mode}",
                userTag, localAudiobooks.Count, absItems.Count, matches.Count, changedMatches.Count, ignoreCutoff ? " (manual sync — cutoff ignored)" : "");

            foreach (var match in changedMatches)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var localTimestamp = match.LocalBook.ReadingProgress?.ModifiedAt ?? DateTimeOffset.MinValue;
                var absTimestamp = match.AbsProgress?.LastUpdateUtc ?? DateTimeOffset.MinValue;

                try
                {
                    if (absTimestamp > localTimestamp)
                        await PullFromAbs(match);
                    else if (localTimestamp > absTimestamp)
                        await PushToAbs(match, integration, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "AudioBookShelf: failed to reconcile \"{Title}\"", match.LocalBook.Title);
                }
            }

            // Echo suppression depends on LastSyncedAt being stamped AFTER all push/pull work for
            // this user — moving this earlier would loop just-applied changes back through the
            // next tick. See the XML doc on Sync.
            await repository.UpdateLastSynced(integration.UserUuid, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AudioBookShelf: sync failed for user {UserTag}", userTag);
        }
    }

    public async Task<bool> VerifyConnection(string serverUrl, string apiToken)
    {
        try
        {
            var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "api/me"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("AudioBookShelf verify failed: {Status} from {Url}", (int)response.StatusCode, baseUri);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AudioBookShelf verify threw against {ServerUrl}", serverUrl);
            return false;
        }
    }

    private async Task PullFromAbs(MatchedAudiobook match)
    {
        var book = match.LocalBook;
        var abs = match.AbsProgress;
        if (abs is null)
            return;

        var existing = await readingStateRepository.GetByBookUuid(book.BookUuid);

        var status = abs.IsFinished
            ? ReadingStatus.Finished
            : abs.Progress > 0
                ? ReadingStatus.Reading
                : ReadingStatus.ReadyToRead;

        var lastUpdateIso = abs.LastUpdateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var nowIso = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var progressPercent = (decimal)Math.Round(abs.Progress * 100.0, 4);
        var remainingTimeMinutes = abs.Duration > abs.CurrentTime
            ? (int)Math.Round((abs.Duration - abs.CurrentTime) / 60.0)
            : 0;

        var chapter = book.Chapters.FirstOrDefault(c => abs.CurrentTime >= c.StartTime && abs.CurrentTime < c.EndTime)
                      ?? book.Chapters.FirstOrDefault();
        var chapterIndex = chapter?.ChapterIndex ?? 0;
        var positionMs = chapter is null
            ? (long)Math.Round(abs.CurrentTime * 1000)
            : (long)Math.Round((abs.CurrentTime - chapter.StartTime) * 1000);

        var timesStarted = existing?.StatusInfo.TimesStartedReading ?? (status == ReadingStatus.ReadyToRead ? 0 : 1);
        var lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading
                              ?? (status == ReadingStatus.ReadyToRead ? null : lastUpdateIso);

        var state = new KoboStoreReadingState
        {
            EntitlementId = book.EntitlementUuid.ToString(),
            Created = existing?.Created ?? book.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LastModified = lastUpdateIso,
            PriorityTimestamp = nowIso,
            StatusInfo = new KoboStoreStatusInfo
            {
                LastModified = lastUpdateIso,
                Status = status.ToString(),
                TimesStartedReading = timesStarted,
                LastTimeStartedReading = lastTimeStarted
            },
            Statistics = new KoboStoreStatistics
            {
                LastModified = lastUpdateIso,
                SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes,
                RemainingTimeMinutes = remainingTimeMinutes
            },
            CurrentBookmark = new KoboStoreBookmark
            {
                LastModified = lastUpdateIso,
                ProgressPercent = progressPercent,
                ContentSourceProgressPercent = existing?.CurrentBookmark.ContentSourceProgressPercent ?? progressPercent,
                Location = new KoboStoreBookmarkLocation
                {
                    Value = abs.CurrentTime.ToString("0.##", CultureInfo.InvariantCulture),
                    Type = "AudioTimestamp",
                    Source = $"audiobook:///{book.EntitlementUuid}/{chapterIndex}?position={positionMs}"
                }
            }
        };

        await readingStateRepository.Upsert(book.BookUuid, state);

        logger.LogInformation("AudioBookShelf: pulled \"{Title}\" ← ABS at {Percent:F1}% ({Status}, {Position})\n  Alexandria: {LocalTs:yyyy-MM-dd HH:mm:ss zzz}\n  ABS:        {AbsTs:yyyy-MM-dd HH:mm:ss zzz}",
            book.Title, abs.Progress * 100, status, FormatDuration(abs.CurrentTime),
            book.ReadingProgress?.ModifiedAt ?? DateTimeOffset.MinValue,
            abs.LastUpdateUtc);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
            return "0m";
        var hours = (int)(seconds / 3600);
        var minutes = (int)Math.Round((seconds % 3600) / 60.0);
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    private async Task PushToAbs(MatchedAudiobook match, AudioBookShelfIntegration integration, CancellationToken cancellationToken)
    {
        var book = match.LocalBook;
        var progress = book.ReadingProgress;
        if (progress is null)
            return;

        if (book.File.Duration is not int duration || duration <= 0)
        {
            logger.LogWarning("AudioBookShelf: skipped push for \"{Title}\" — missing duration", book.Title);
            return;
        }

        var progressFraction = progress.Status == ReadingStatus.Finished
            ? 1.0
            : Math.Clamp((double)(progress.ProgressPercent ?? 0) / 100.0, 0.0, 1.0);
        var currentTime = progressFraction * duration;

        // ABS computes its UI-facing `progress` field from a listening session, not from PATCH
        // /api/me/progress (which only updates currentTime + isFinished). Open → sync → close a
        // session so ABS recomputes progress and stops marking the session as still-open. The 1-s
        // timeListened on sync is deliberately non-zero: with 0, ABS treats the session as inert
        // and `/close` returns 200 but leaves the session in the open list. 1 s is the smallest
        // value that makes /close actually mark closedAt and remove it from /sessions/open.
        var sessionId = await OpenAbsSession(integration, match.AbsItem.Id, cancellationToken);
        try
        {
            await SyncAbsSession(integration.ServerUrl, integration.ApiToken, sessionId, currentTime, duration, cancellationToken);
        }
        finally
        {
            await CloseAbsSession(integration.ServerUrl, integration.ApiToken, sessionId, currentTime, duration, cancellationToken);
        }

        if (progress.Status == ReadingStatus.Finished)
            await PatchIsFinished(integration.ServerUrl, integration.ApiToken, match.AbsItem.Id, cancellationToken);

        logger.LogInformation("AudioBookShelf: pushed \"{Title}\" → ABS at {Percent:F1}% ({Position}{Finished})\n  Alexandria: {LocalTs:yyyy-MM-dd HH:mm:ss zzz}\n  ABS:        {AbsTs:yyyy-MM-dd HH:mm:ss zzz}",
            book.Title, progressFraction * 100, FormatDuration(currentTime), progress.Status == ReadingStatus.Finished ? ", finished" : "",
            progress.ModifiedAt,
            match.AbsProgress?.LastUpdateUtc ?? DateTimeOffset.MinValue);
    }

    private List<MatchedAudiobook> MatchByFilename(List<Book> localAudiobooks, List<AbsItem> absItems, Dictionary<string, AbsMediaProgress> absProgressByItemId)
    {
        var absByKey = new Dictionary<string, AbsItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in absItems)
        {
            foreach (var key in BuildAbsKeys(item))
            {
                if (!absByKey.TryAdd(key, item))
                    logger.LogDebug("AudioBookShelf: filename collision on \"{Key}\" — kept {Kept}, ignored {Dropped}", key, absByKey[key].Id, item.Id);
            }
        }

        var matches = new List<MatchedAudiobook>();
        foreach (var book in localAudiobooks)
        {
            foreach (var key in BuildLocalKeys(book))
            {
                if (absByKey.TryGetValue(key, out var item))
                {
                    absProgressByItemId.TryGetValue(item.Id, out var progress);
                    var localTimestamp = book.ReadingProgress?.ModifiedAt ?? DateTimeOffset.MinValue;
                    var absTimestamp = progress?.LastUpdateUtc ?? DateTimeOffset.MinValue;
                    matches.Add(new MatchedAudiobook
                    {
                        LocalBook = book,
                        AbsItem = item,
                        AbsProgress = progress,
                        LastUpdatedAt = localTimestamp > absTimestamp ? localTimestamp : absTimestamp
                    });
                    break;
                }
            }
        }
        return matches;
    }

    private static IEnumerable<string> BuildLocalKeys(Book book)
    {
        if (!string.IsNullOrEmpty(book.File.SourcePath))
            yield return Path.GetFileName(book.File.SourcePath);

        foreach (var chapter in book.Chapters)
        {
            if (!string.IsNullOrEmpty(chapter.FileName))
                yield return Path.GetFileName(chapter.FileName);
        }
    }

    private static IEnumerable<string> BuildAbsKeys(AbsItem item)
    {
        if (!string.IsNullOrEmpty(item.RelPath))
            yield return Path.GetFileName(item.RelPath);

        if (!string.IsNullOrEmpty(item.Path))
            yield return Path.GetFileName(item.Path);

        if (item.Media?.AudioFiles is { } files)
        {
            foreach (var file in files)
            {
                var name = file.Metadata?.Filename;
                if (!string.IsNullOrEmpty(name))
                    yield return name;
            }
        }
    }

    private async Task<Dictionary<string, AbsMediaProgress>> FetchMediaProgress(string serverUrl, string apiToken, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        var me = await SendForJson<AbsMe>(HttpMethod.Get, new Uri(baseUri, "api/me"), apiToken, null, cancellationToken);
        var dict = new Dictionary<string, AbsMediaProgress>();
        foreach (var progress in me?.MediaProgress ?? [])
        {
            if (!string.IsNullOrEmpty(progress.LibraryItemId))
                dict[progress.LibraryItemId] = progress;
        }
        return dict;
    }

    private async Task<List<AbsItem>> FetchAllAbsItems(string serverUrl, string apiToken, CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        var libraries = await SendForJson<AbsLibrariesResponse>(HttpMethod.Get, new Uri(baseUri, "api/libraries"), apiToken, null, cancellationToken);

        var items = new List<AbsItem>();
        foreach (var library in libraries?.Libraries ?? [])
        {
            var page = 0;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return items;

                var response = await SendForJson<AbsItemsResponse>(HttpMethod.Get, new Uri(baseUri, $"api/libraries/{library.Id}/items?limit={pageSize}&page={page}"), apiToken, null, cancellationToken);
                if (response?.Results is not { Count: > 0 } results)
                    break;

                items.AddRange(results);
                if (results.Count < pageSize)
                    break;

                page++;
            }
        }
        return items;
    }

    private async Task<T?> SendForJson<T>(HttpMethod method, Uri uri, string apiToken, object? payload, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendCore(method, uri, apiToken, payload, cts.Token);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cts.Token);
    }

    private async Task Send(HttpMethod method, Uri uri, string apiToken, object? payload, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendCore(method, uri, apiToken, payload, cts.Token);
    }

    private async Task<HttpResponseMessage> SendCore(HttpMethod method, Uri uri, string apiToken, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<string> OpenAbsSession(AudioBookShelfIntegration integration, string libraryItemId, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(integration.ServerUrl.TrimEnd('/') + "/");
        var payload = new AbsPlayRequest
        {
            DeviceInfo = new AbsDeviceInfo { DeviceId = $"alexandria-{integration.UserUuid}", ClientName = "Alexandria", ClientVersion = "1.0.0" },
            SupportedMimeTypes = ["audio/mpeg", "audio/mp4"],
            MediaPlayer = "audio",
            ForceDirectPlay = false,
            ForceTranscode = false
        };

        var response = await SendForJson<AbsSession>(HttpMethod.Post, new Uri(baseUri, $"api/items/{libraryItemId}/play"), integration.ApiToken, payload, cancellationToken);
        if (response?.Id is null)
            throw new InvalidOperationException($"AudioBookShelf /play returned no session id for item {libraryItemId}");
        return response.Id;
    }

    private Task SyncAbsSession(string serverUrl, string apiToken, string sessionId, double currentTime, double duration, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        var payload = new AbsSessionUpdate { CurrentTime = currentTime, Duration = duration, TimeListened = 1 };
        return Send(HttpMethod.Post, new Uri(baseUri, $"api/session/{sessionId}/sync"), apiToken, payload, cancellationToken);
    }

    private Task CloseAbsSession(string serverUrl, string apiToken, string sessionId, double currentTime, double duration, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        var payload = new AbsSessionUpdate { CurrentTime = currentTime, Duration = duration, TimeListened = 1 };
        return Send(HttpMethod.Post, new Uri(baseUri, $"api/session/{sessionId}/close"), apiToken, payload, cancellationToken);
    }

    private Task PatchIsFinished(string serverUrl, string apiToken, string libraryItemId, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        return Send(HttpMethod.Patch, new Uri(baseUri, $"api/me/progress/{libraryItemId}"), apiToken, new { isFinished = true }, cancellationToken);
    }

    private sealed class MatchedAudiobook
    {
        public Book LocalBook { get; init; } = new();
        public AbsItem AbsItem { get; init; } = new();
        public AbsMediaProgress? AbsProgress { get; init; }
        public DateTimeOffset LastUpdatedAt { get; init; }
    }

    private sealed class AbsMe
    {
        [JsonPropertyName("mediaProgress")]
        public List<AbsMediaProgress> MediaProgress { get; init; } = [];
    }

    private sealed class AbsPlayRequest
    {
        [JsonPropertyName("deviceInfo")]
        public AbsDeviceInfo DeviceInfo { get; init; } = new();

        [JsonPropertyName("supportedMimeTypes")]
        public List<string> SupportedMimeTypes { get; init; } = [];

        [JsonPropertyName("mediaPlayer")]
        public string MediaPlayer { get; init; } = "audio";

        [JsonPropertyName("forceDirectPlay")]
        public bool ForceDirectPlay { get; init; }

        [JsonPropertyName("forceTranscode")]
        public bool ForceTranscode { get; init; }
    }

    private sealed class AbsDeviceInfo
    {
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; init; } = string.Empty;

        [JsonPropertyName("clientName")]
        public string ClientName { get; init; } = string.Empty;

        [JsonPropertyName("clientVersion")]
        public string ClientVersion { get; init; } = string.Empty;
    }

    private sealed class AbsSession
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class AbsSessionUpdate
    {
        [JsonPropertyName("currentTime")]
        public double CurrentTime { get; init; }

        [JsonPropertyName("duration")]
        public double Duration { get; init; }

        [JsonPropertyName("timeListened")]
        public double TimeListened { get; init; }
    }

    private sealed class AbsMediaProgress
    {
        [JsonPropertyName("libraryItemId")]
        public string LibraryItemId { get; init; } = string.Empty;

        // ABS reports lastUpdate as Unix epoch milliseconds.
        [JsonPropertyName("lastUpdate")]
        public long LastUpdate { get; init; }

        [JsonPropertyName("progress")]
        public double Progress { get; init; }

        [JsonPropertyName("isFinished")]
        public bool IsFinished { get; init; }

        [JsonPropertyName("currentTime")]
        public double CurrentTime { get; init; }

        [JsonPropertyName("duration")]
        public double Duration { get; init; }

        public DateTimeOffset LastUpdateUtc => DateTimeOffset.FromUnixTimeMilliseconds(LastUpdate);
    }

    private sealed class AbsLibrariesResponse
    {
        [JsonPropertyName("libraries")]
        public List<AbsLibrary> Libraries { get; init; } = [];
    }

    private sealed class AbsLibrary
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class AbsItemsResponse
    {
        [JsonPropertyName("results")]
        public List<AbsItem> Results { get; init; } = [];
    }

    private sealed class AbsItem
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("relPath")]
        public string? RelPath { get; init; }

        [JsonPropertyName("media")]
        public AbsMedia? Media { get; init; }
    }

    private sealed class AbsMedia
    {
        [JsonPropertyName("audioFiles")]
        public List<AbsAudioFile> AudioFiles { get; init; } = [];
    }

    private sealed class AbsAudioFile
    {
        [JsonPropertyName("metadata")]
        public AbsAudioFileMetadata? Metadata { get; init; }
    }

    private sealed class AbsAudioFileMetadata
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; init; }
    }
}
