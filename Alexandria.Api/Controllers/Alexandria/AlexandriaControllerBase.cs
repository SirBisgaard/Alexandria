using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

/// <summary>
/// Shared base for controllers that live under <c>/api/alexandria</c> and authenticate
/// via the Alexandria scheme. Centralizes the <see cref="ApiControllerAttribute"/> and
/// authorization attributes so individual controllers only declare their own route.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "Alexandria")]
public abstract class AlexandriaControllerBase : ControllerBase
{
}
