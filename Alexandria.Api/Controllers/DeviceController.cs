using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("/api/alexandria/devices")]
[Authorize(AuthenticationSchemes = "Alexandria")]
public class DeviceController(IKoboDeviceRepository deviceRepository, IKoboDevicePairingService pairingService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await deviceRepository.GetAll();
        return Ok(devices);
    }

    [HttpPut("{uuid:guid}")]
    public async Task<IActionResult> UpdateDevice([FromRoute] Guid uuid, [FromBody] UpdateDeviceRequest request)
    {
        var device = await deviceRepository.GetByUuid(uuid);
        if (device is null)
            return NotFound();

        device.ModelName = request.ModelName;
        await deviceRepository.Update(device);
        return Ok(device);
    }

    [HttpDelete("{uuid:guid}")]
    public async Task<IActionResult> DeleteDevice([FromRoute] Guid uuid)
    {
        await deviceRepository.Delete(uuid);
        return NoContent();
    }

    [HttpGet("pairing")]
    public IActionResult GetPairingStatus()
    {
        var expiry = pairingService.GetPairingExpiry();
        return Ok(new
        {
            enabled = expiry is not null,
            expiresAt = expiry
        });
    }

    [HttpPost("pairing")]
    public IActionResult EnablePairing()
    {
        pairingService.EnablePairing(TimeSpan.FromMinutes(5));
        return Ok(new
        {
            enabled = true,
            expiresAt = pairingService.GetPairingExpiry()
        });
    }

    [HttpDelete("pairing")]
    public IActionResult DisablePairing()
    {
        pairingService.DisablePairing();
        return NoContent();
    }
}

public record UpdateDeviceRequest(string? ModelName) { }
