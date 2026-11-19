using EMarket.BLL.AfterSale;
using EMarket.BLL.Finance;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.AfterSale;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1/returns")]
public sealed class ReturnsController(ReturnService returns) : ApiControllerBase
{
    private string Role => User.IsInRole(Roles.Admin) ? "admin" : "buyer";

    [HttpPost("orders/{orderId:guid}")]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Create(Guid orderId, [FromForm] string payload, [FromForm] List<IFormFile>? photos, CancellationToken ct)
    {
        var input = System.Text.Json.JsonSerializer.Deserialize<ReturnRequestInput>(payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new BLL.Common.AppException(400, Messages.Invalid);
        if (!TryValidateModel(input))
        {
            return ValidationProblem(ModelState);
        }

        var id = await returns.RequestAsync(UserId, orderId, input, (photos ?? []).Select(ToUpload).ToList(), ct);
        return StatusCode(StatusCodes.Status201Created, new { id, message = Messages.ReturnSent });
    }

    [HttpGet]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    public Task<PagedResult<ReturnDto>> Mine([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        returns.ListAsync(UserId, null, false, status, page, ct);

    [HttpGet("{id:guid}")]
    public Task<ReturnDto> Get(Guid id, CancellationToken ct) => returns.GetAsync(id, UserId, Role, ct);

    [HttpGet("{id:guid}/photos/{index:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Photo(Guid id, int index, CancellationToken ct)
    {
        var role = User.IsInRole(Roles.Admin) ? "admin" : User.IsInRole(Roles.Seller) ? "seller" : "buyer";
        Stream stream;
        try
        {
            stream = await returns.OpenPhotoAsync(id, index, UserId, role, ct);
        }
        catch (BLL.Common.AppException) when (role == "seller")
        {
            stream = await returns.OpenPhotoAsync(id, index, UserId, "buyer", ct); // a seller may also be the buyer of this order
        }

        return File(stream, "image/jpeg");
    }

    [HttpPost("{id:guid}/escalate")]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    public async Task<IActionResult> Escalate(Guid id, CancellationToken ct)
    {
        await returns.EscalateAsync(UserId, id, ct);
        return NoContent();
    }
}

[Route("api/v1/seller/returns")]
[Authorize(Roles = Roles.Seller)]
public sealed class SellerReturnsController(ReturnService returns) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReturnDto>> List([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        returns.ListAsync(null, UserId, false, status, page, ct);

    [HttpGet("{id:guid}")]
    public Task<ReturnDto> Get(Guid id, CancellationToken ct) => returns.GetAsync(id, UserId, "seller", ct);

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, DecisionRequest request, CancellationToken ct)
    {
        await returns.SellerDecideAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/item-received")]
    public async Task<IActionResult> ItemReceived(Guid id, ItemReceivedRequest request, CancellationToken ct)
    {
        await returns.ItemReceivedAsync(UserId, id, request.Resellable, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/returns")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReturnAdminController(ReturnService returns) : ApiControllerBase
{
    /// <summary>With no status: the queue of escalated requests and those the seller left for 3 days.</summary>
    [HttpGet]
    public Task<PagedResult<ReturnDto>> List([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        returns.ListAsync(null, null, true, status, page, ct);

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, DecisionRequest request, CancellationToken ct)
    {
        await returns.AdminDecideAsync(UserId, id, request, ct);
        return NoContent();
    }
}

[Route("api/v1/reviews")]
public sealed class ReviewsController(ReviewService reviews) : ApiControllerBase
{
    [AllowAnonymous, HttpGet("products/{productId:guid}")]
    public Task<PagedResult<ReviewDto>> ForProduct(Guid productId, [FromQuery] int page = 1, CancellationToken ct = default) =>
        reviews.ForProductAsync(productId, page, ct);

    [HttpPost("order-items/{orderItemId:guid}")]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    public async Task<IActionResult> Create(Guid orderItemId, ReviewInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await reviews.CreateAsync(UserId, orderItemId, input, ct), message = Messages.ReviewPosted });

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    public async Task<IActionResult> Update(Guid id, ReviewInput input, CancellationToken ct)
    {
        await reviews.UpdateAsync(UserId, id, input, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await reviews.DeleteAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, ReportInput input, CancellationToken ct)
    {
        await reviews.ReportAsync(UserId, id, input.Reason, ct);
        return NoContent();
    }
}

[Route("api/v1/seller/reviews")]
[Authorize(Roles = Roles.Seller)]
public sealed class SellerReviewsController(ReviewService reviews) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? productId, [FromQuery] int? stars, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var (items, average, count) = await reviews.ForSellerAsync(UserId, productId, stars, page, ct);
        return Ok(new { average, count, reviews = items });
    }

    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, ReplyInput input, CancellationToken ct)
    {
        await reviews.ReplyAsync(UserId, id, input.Reply, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/reviews")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReviewAdminController(ReviewService reviews) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReviewDto>> List([FromQuery] bool reportedOnly, [FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        reviews.AdminListAsync(reportedOnly, status, page, ct);

    [HttpPost("{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await reviews.SetHiddenAsync(UserId, id, true, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/show")]
    public async Task<IActionResult> Show(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await reviews.SetHiddenAsync(UserId, id, false, request.Reason, ct);
        return NoContent();
    }
}

[Route("api/v1/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] bool unreadOnly, [FromQuery] int page = 1, CancellationToken ct = default) =>
        notifications.ListAsync(UserId, unreadOnly, page, ct);

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) => Ok(new { count = await notifications.UnreadCountAsync(UserId, ct) });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkReadAsync(UserId, null, ct);
        return NoContent();
    }
}

[Route("api/v1/seller")]
[Authorize(Roles = Roles.Seller)]
public sealed class SellerEarningsController(EarningsService earnings) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public Task<SellerDashboardDto> Dashboard(CancellationToken ct) => earnings.DashboardAsync(UserId, ct);

    [HttpGet("earnings")]
    public Task<IReadOnlyList<EarningRowDto>> Earnings([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        earnings.EarningsAsync(UserId, from, to, ct);

    [HttpGet("payouts")]
    public Task<PagedResult<PayoutDto>> Payouts([FromQuery] int page = 1, CancellationToken ct = default) => earnings.PayoutsAsync(UserId, null, page, ct);

    [HttpPost("payouts")]
    public async Task<IActionResult> RequestPayout(PayoutRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await earnings.RequestPayoutAsync(UserId, request.Amount, ct), message = Messages.PayoutSent });

    [HttpPost("payouts/{id:guid}/cancel")]
    public async Task<IActionResult> CancelPayout(Guid id, CancellationToken ct)
    {
        await earnings.CancelPayoutAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpGet("shop")]
    public Task<ShopSettingsDto> Shop(CancellationToken ct) => earnings.ShopAsync(UserId, ct);

    [HttpPut("shop")]
    public async Task<IActionResult> UpdateShop(ShopSettingsRequest request, CancellationToken ct)
    {
        await earnings.UpdateShopAsync(UserId, request, ct);
        return Ok(new { message = Messages.Saved });
    }

    [HttpPost("shop/{kind:regex(^(logo|banner)$)}")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> ShopImage(string kind, IFormFile file, CancellationToken ct) =>
        Ok(new { path = await earnings.SetShopImageAsync(UserId, kind, ToUpload(file), ct) });

    [HttpPut("shop/payout-details")]
    public async Task<IActionResult> PayoutDetails(PayoutDetailsRequest request, CancellationToken ct)
    {
        await earnings.UpdatePayoutDetailsAsync(UserId, request, ct);
        return Ok(new { message = Messages.Saved });
    }
}

[Route("api/v1/admin/payouts")]
[Authorize(Roles = Roles.Admin)]
public sealed class PayoutAdminController(EarningsService earnings) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<PayoutDto>> List([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        earnings.PayoutsAsync(null, status, page, ct);

    [HttpPost("{id:guid}/pay")]
    public async Task<IActionResult> Pay(Guid id, PayPayoutRequest request, CancellationToken ct)
    {
        await earnings.PayAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await earnings.RejectPayoutAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }
}
