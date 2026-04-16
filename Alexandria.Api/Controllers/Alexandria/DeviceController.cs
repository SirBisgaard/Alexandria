using Alexandria.Api.Models.Requests;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/devices")]
public class DeviceController(IKoboDeviceRepository deviceRepository) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await deviceRepository.GetAll();
        return Ok(devices);
    }

    [HttpGet("generate-key")]
    public IActionResult GenerateKey()
    {
        return Ok(new { deviceUuid = Guid.NewGuid() });
    }

    [HttpPost]
    public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceRequest request)
    {
        var device = new KoboDevice
        {
            DeviceUuid = request.DeviceUuid ?? Guid.NewGuid(),
            Nickname = request.Nickname
        };

        await deviceRepository.Create(device);

        var created = await deviceRepository.GetByUuid(device.DeviceUuid);
        return Ok(created);
    }

    [HttpPut("{uuid:guid}")]
    public async Task<IActionResult> UpdateDevice([FromRoute] Guid uuid, [FromBody] UpdateDeviceRequest request)
    {
        var device = await deviceRepository.GetByUuid(uuid);
        if (device is null)
            return NotFound();

        device.Nickname = request.Nickname;
        await deviceRepository.Update(device);
        return Ok(device);
    }

    [HttpDelete("{uuid:guid}")]
    public async Task<IActionResult> DeleteDevice([FromRoute] Guid uuid)
    {
        await deviceRepository.Delete(uuid);
        return NoContent();
    }

    [HttpPost("{uuid:guid}/unlock")]
    public async Task<IActionResult> UnlockDevice([FromRoute] Guid uuid)
    {
        var device = await deviceRepository.GetByUuid(uuid);
        if (device is null)
            return NotFound();

        await deviceRepository.SetLocked(uuid, false);
        return Ok();
    }
}
