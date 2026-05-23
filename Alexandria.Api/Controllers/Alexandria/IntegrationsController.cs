using System.Security.Claims;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Integrations.Models;
using Alexandria.Business.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/integrations")]
public class IntegrationsController(IIntegrationService integrationService, ILogger<IntegrationsController> logger) : AlexandriaControllerBase
{
    [HttpGet("audiobookshelf")]
    public async Task<IActionResult> GetAudioBookShelf()
    {
        var userUuid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var integration = await integrationService.GetAudioBookShelf(userUuid);
        return Ok(new
        {
            enabled = integration?.Enabled ?? false,
            serverUrl = integration?.ServerUrl ?? string.Empty,
            apiToken = integration?.ApiToken ?? string.Empty,
            lastSyncedAt = integration?.LastSyncedAt
        });
    }

    [HttpPut("audiobookshelf")]
    public async Task<IActionResult> UpsertAudioBookShelf([FromBody] UpdateAudioBookShelfIntegrationRequest request)
    {
        var userUuid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        logger.LogInformation("Upserting AudioBookShelf integration for user {UserUuid}", userUuid);

        if (request.Enabled)
        {
            if (string.IsNullOrWhiteSpace(request.ServerUrl) || string.IsNullOrWhiteSpace(request.ApiToken))
                return BadRequest(new { error = "Server url and API token are required when enabling the integration." });

            if (!Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out var serverUri) || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
                return BadRequest(new { error = "Server url must be an absolute http or https URL." });

            var connectionOk = await integrationService.VerifyAudioBookShelfConnection(request.ServerUrl, request.ApiToken);
            if (!connectionOk)
                return BadRequest(new { error = "Could not connect to AudioBookShelf with the provided server url and API token." });
        }

        await integrationService.UpsertAudioBookShelf(new AudioBookShelfIntegration
        {
            UserUuid = userUuid,
            Enabled = request.Enabled,
            ServerUrl = request.ServerUrl,
            ApiToken = request.ApiToken
        });

        return NoContent();
    }

    [HttpPost("audiobookshelf/sync")]
    public async Task<IActionResult> SyncAudioBookShelfNow(CancellationToken cancellationToken)
    {
        var userUuid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        logger.LogInformation("Manual AudioBookShelf sync requested by user {UserUuid}", userUuid);
        await integrationService.SyncAudioBookShelfNow(userUuid, cancellationToken);
        return NoContent();
    }
}
