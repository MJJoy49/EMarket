using EMarket.BLL.Sellers;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1/seller-applications")]
public sealed class SellerApplicationsController(SellerApplicationService applications) : ApiControllerBase
{
    [HttpPost]
    [Authorize(Roles = Roles.Buyer)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Apply(
        [FromForm] SellerApplicationRequest request, IFormFile nidFront, IFormFile nidBack, IFormFile? tradeLicence, CancellationToken ct)
    {
        var id = await applications.ApplyAsync(UserId, request,
            new ApplicationFiles(ToUpload(nidFront), ToUpload(nidBack), ToUploadOrNull(tradeLicence)), ct);
        return StatusCode(StatusCodes.Status201Created, new { id, message = Messages.ApplicationSent });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        await applications.LatestForUserAsync(UserId, ct) is { } application ? Ok(application) : NoContent();
}

[Route("api/v1/admin/seller-applications")]
[Authorize(Roles = Roles.Admin)]
public sealed class SellerApplicationsAdminController(SellerApplicationService applications) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<SellerApplicationDto>> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) =>
        applications.ListAsync(status, search, page, ct);

    [HttpGet("{id:guid}")]
    public Task<SellerApplicationDto> Get(Guid id, CancellationToken ct) => applications.GetForAdminAsync(UserId, id, ct);

    /// <summary>Identity documents are never cached (NFR-44).</summary>
    [HttpGet("{id:guid}/documents/{document}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Document(Guid id, string document, CancellationToken ct)
    {
        var stream = await applications.OpenDocumentAsync(UserId, id, document, ct);
        return File(stream, "application/octet-stream");
    }

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, ReviewDecisionRequest request, CancellationToken ct)
    {
        await applications.DecideAsync(UserId, id, request, ct);
        return NoContent();
    }
}
