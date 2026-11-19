using EMarket.BLL.Profile;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Auth;
using EMarket.Shared.Dtos.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1/profile")]
public sealed class ProfileController(ProfileService profiles) : ApiControllerBase
{
    [HttpGet]
    public Task<ProfileDto> Get(CancellationToken ct) => profiles.GetAsync(UserId, ct);

    [HttpGet("credit-history")]
    public Task<IReadOnlyList<CreditLineDto>> CreditHistory(CancellationToken ct) => profiles.CreditHistoryAsync(UserId, ct);

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken ct)
    {
        var emailChangeStarted = await profiles.UpdateAsync(UserId, request, ct);
        return Ok(new { message = Messages.Saved, emailChangeStarted });
    }

    [HttpPost("photo")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Photo(IFormFile file, CancellationToken ct) =>
        Ok(new { photoUrl = await profiles.SetPhotoAsync(UserId, ToUpload(file), ct) });

    [AllowAnonymous, HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(TokenRequest request, CancellationToken ct)
    {
        await profiles.ConfirmEmailChangeAsync(request.Token, ct);
        return NoContent();
    }
}

[Route("api/v1/addresses")]
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
public sealed class AddressesController(AddressService addresses) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AddressDto>> List(CancellationToken ct) => addresses.ListAsync(UserId, ct);

    [HttpPost]
    public async Task<IActionResult> Add(SaveAddressRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await addresses.AddAsync(UserId, request, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveAddressRequest request, CancellationToken ct)
    {
        await addresses.UpdateAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        await addresses.SetDefaultAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await addresses.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}

[Route("api/v1/locations")]
[AllowAnonymous]
public sealed class LocationsController(ProfileService profiles) : ApiControllerBase
{
    /// <summary>Divisions when no parent is given; otherwise the districts or areas under it.</summary>
    [HttpGet]
    public Task<IReadOnlyList<LocationDto>> List([FromQuery] Guid? parentId, CancellationToken ct) => profiles.LocationsAsync(parentId, ct);
}
