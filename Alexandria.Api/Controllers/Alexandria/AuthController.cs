using System.Security.Claims;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Authentication;
using Alexandria.Business.Authentication.Interfaces;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[ApiController]
[Route("/api/alexandria/auth")]
[Authorize(AuthenticationSchemes = "Alexandria")]
public class AuthController(IUserRepository userRepository, IPasswordHasher passwordHasher, AuthSecretProvider authSecrets) : AlexandriaControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await userRepository.GetByUsername(request.Username);
        if (user is null)
            return Unauthorized(new { error = "Invalid username or password" });

        if (!passwordHasher.Verify(request.Password, user.Password))
            return Unauthorized(new { error = "Invalid username or password" });

        var token = TokenHelpers.Mint(user.UserUuid.ToString(), authSecrets.GetUserHmacKey(), TokenHelpers.UserTokenTtl);

        await userRepository.UpdateLastLogin(user.UserUuid);

        return Ok(new { user.UserUuid, user.DisplayName, Token = token });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var userUuid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = User.FindFirstValue(ClaimTypes.Name);
        var username = User.FindFirstValue("username");

        return Ok(new { userUuid, displayName, username });
    }
}
