using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Api.Models.Responses;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/file-management")]
public class FileManagementController(IFileManagementService renameService, IFileManagementProgressService progressService, ILogger<FileManagementController> logger) : AlexandriaControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [HttpPost("dry-run")]
    public async Task<IActionResult> DryRun()
    {
        try
        {
            var plan = await renameService.ComputePlan();
            return Ok(ToResponse(plan));
        }
        catch (TemplateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task Run()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        progressService.Subscribe(
            new ChannelProgress<FileManagementReport>(channel.Writer, r => FormatFrame(new FileManagementProgressEvent
            {
                Title = r.Title,
                Progress = r.Ordinal,
                Total = r.Total
            })),
            new ChannelProgress<FileManagementCompletion>(channel.Writer, c => FormatFrame(new FileManagementCompleteEvent
            {
                Done = true,
                Renamed = c.Renamed,
                Skipped = c.Skipped,
                Failed = c.Failed.Select(f => new FileManagementFailureEvent { BookUuid = f.BookUuid, Error = f.Error }).ToList()
            })));

        var runTask = Task.Run(async () =>
        {
            try
            {
                await renameService.Run(HttpContext.RequestAborted);
            }
            catch (FileManagementConflictException ex)
            {
                channel.Writer.TryWrite(FormatFrame(new FileManagementErrorEvent
                {
                    Done = true,
                    Failed = true,
                    Error = ex.Message,
                    Conflicts = ex.Conflicts
                }));
            }
            catch (TemplateException ex)
            {
                channel.Writer.TryWrite(FormatFrame(new FileManagementErrorEvent { Done = true, Failed = true, Error = ex.Message }));
            }
            catch (InvalidOperationException ex)
            {
                channel.Writer.TryWrite(FormatFrame(new FileManagementErrorEvent { Done = true, Failed = true, Error = ex.Message }));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "File management failed unexpectedly");
                channel.Writer.TryWrite(FormatFrame(new FileManagementErrorEvent { Done = true, Failed = true, Error = "File management failed unexpectedly." }));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(HttpContext.RequestAborted))
            {
                await Response.WriteAsync(frame);
                await Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Mass-rename SSE subscriber disconnected");
        }

        await runTask;
    }

    private static FileManagementPlanResponse ToResponse(FileManagementPlan plan)
    {
        return new FileManagementPlanResponse
        {
            Template = plan.Template,
            Operations = plan.Operations.Select(o => new FileManagementOperationResponse
            {
                BookUuid = o.BookUuid,
                Title = o.Title,
                MediaType = o.MediaType.ToString(),
                CurrentPath = o.CurrentPath,
                NewPath = o.NewPath,
                ChapterRenameCount = o.ChapterRenames.Count
            }).ToList(),
            Skipped = plan.Skipped.Select(s => new FileManagementSkippedResponse
            {
                BookUuid = s.BookUuid,
                Title = s.Title,
                CurrentPath = s.CurrentPath
            }).ToList(),
            Conflicts = plan.Conflicts
        };
    }

    private static string FormatFrame<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return $"data: {json}\n\n";
    }

    private sealed class ChannelProgress<T>(ChannelWriter<string> writer, Func<T, string> formatter) : IProgress<T>
    {
        public void Report(T value) => writer.TryWrite(formatter(value));
    }
}
