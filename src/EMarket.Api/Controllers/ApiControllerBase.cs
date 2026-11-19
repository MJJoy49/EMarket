using System.Security.Claims;
using EMarket.BLL.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EMarket.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    protected string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected static UploadFile ToUpload(IFormFile file) => new(file.OpenReadStream(), file.Length);

    protected static UploadFile? ToUploadOrNull(IFormFile? file) => file is null ? null : ToUpload(file);
}
