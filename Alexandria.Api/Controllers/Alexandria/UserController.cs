using System.Security.Claims;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Alexandria.DataAccess.Database.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/users")]
public class UserController(IUserRepository userRepository, ISettingsRepository settingsRepository) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await userRepository.GetAll();
        return Ok(users.Select(u => new
        {
            u.UserUuid, u.Username, u.DisplayName,
            u.CreatedAt, u.ModifiedAt, u.LastLoginAt
        }));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var existing = await userRepository.GetByUsername(request.Username);
        if (existing is not null)
            return BadRequest(new { error = "Username already exists" });

        var settings = await settingsRepository.Get();
        var user = new User
        {
            UserUuid = Guid.NewGuid(),
            Username = request.Username,
            Password = UserRepository.HashPassword(request.Password, settings.UuidSeed),
            DisplayName = request.DisplayName
        };

        await userRepository.Add(user);
        return Ok(new { user.UserUuid, user.Username, user.DisplayName });
    }

    [HttpPut("{uuid:guid}")]
    public async Task<IActionResult> UpdateUser([FromRoute] Guid uuid, [FromBody] UpdateUserRequest request)
    {
        var user = await userRepository.GetByUuid(uuid);
        if (user is null)
            return NotFound();

        user.Username = request.Username;
        user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Password))
        {
            var settings = await settingsRepository.Get();
            user.Password = UserRepository.HashPassword(request.Password, settings.UuidSeed);
        }

        await userRepository.Update(user);
        return Ok(new { user.UserUuid, user.Username, user.DisplayName });
    }

    [HttpDelete("{uuid:guid}")]
    public async Task<IActionResult> DeleteUser([FromRoute] Guid uuid)
    {
        var currentUserUuid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserUuid is not null && Guid.Parse(currentUserUuid) == uuid)
            return BadRequest(new { error = "Cannot delete yourself" });

        var count = await userRepository.Count();
        if (count <= 1)
            return BadRequest(new { error = "Cannot delete the last user" });

        await userRepository.Delete(uuid);
        return NoContent();
    }
}
