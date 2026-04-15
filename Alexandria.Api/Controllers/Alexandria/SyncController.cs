using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

file class ChannelProgress<T>(Func<T, string> formatter, ChannelWriter<string> writer) : IProgress<T>
{
    public void Report(T value) => writer.TryWrite(formatter(value));
}

[Route("/api/alexandria/sync")]
public class SyncController(IAlexandriaLibraryService alexandriaLibraryService, IAlexandriaSettingsService settingsService, AlexandriaLibrarySyncLock alexandriaLibrarySyncLock, ILogger<SyncController> logger) : AlexandriaControllerBase
{
    [HttpPost]
    public async Task Sync()
    {
        if (!alexandriaLibrarySyncLock.TryAcquire())
        {
            logger.LogInformation("Sync requested but one is already in progress — returning 409");
            Response.StatusCode = StatusCodes.Status409Conflict;
            Response.ContentType = "application/json";
            await Response.WriteAsync("""{"error":"sync already in progress"}""");
            return;
        }

        logger.LogInformation("Starting library sync");
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var channel = Channel.CreateUnbounded<string>();

        var progress = new ChannelProgress<(int current, int total)>(value =>
        {
            var json = JsonSerializer.Serialize(new SyncProgressEvent(value.current, value.total), jsonOptions);
            return $"data: {json}\n\n";
        }, channel.Writer);

        var syncTask = Task.Run(async () =>
        {
            try
            {
                var bookCount = await alexandriaLibraryService.Sync(progress);
                var settings = await settingsService.Get();
                var lastIndexed = settings.LastIndexedTimestamp?.ToString("O");
                var doneJson = JsonSerializer.Serialize(new SyncCompleteEvent(true, bookCount, lastIndexed), jsonOptions);
                channel.Writer.TryWrite($"data: {doneJson}\n\n");
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync())
            {
                await Response.WriteAsync(message);
                await Response.Body.FlushAsync();
            }

            await syncTask;
        }
        finally
        {
            alexandriaLibrarySyncLock.Release();
        }
    }
}
