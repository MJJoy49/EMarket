using EMarket.BLL.Admin;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Admin;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class UserAdminController(UserAdminService users) : ApiControllerBase
{
    [HttpGet("users")]
    public Task<PagedResult<UserRowDto>> Users([FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] int page = 1, CancellationToken ct = default) => users.ListAsync(search, role, status, page, ct);

    [HttpGet("users/{id:guid}")]
    public Task<UserDetailDto> GetUser(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost("users/{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await users.BlockAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("users/{id:guid}/unblock")]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken ct)
    {
        await users.UnblockAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await users.DeleteAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }

    /// <summary>The temporary password is returned once and stored nowhere (FR-16.9).</summary>
    [HttpPost("admins")]
    public async Task<IActionResult> CreateAdmin(CreateAdminRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { temporaryPassword = await users.CreateAdminAsync(UserId, request, ct) });

    [HttpGet("shops")]
    public Task<PagedResult<ShopRowDto>> Shops([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) =>
        users.ShopsAsync(status, search, page, ct);

    [HttpPost("shops/{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await users.SetShopSuspendedAsync(UserId, id, true, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("shops/{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await users.SetShopSuspendedAsync(UserId, id, false, request.Reason, ct);
        return NoContent();
    }

    [HttpGet("audit")]
    public Task<PagedResult<AuditRowDto>> Audit([FromQuery] string? action, [FromQuery] Guid? entityId, [FromQuery] int page = 1, CancellationToken ct = default) =>
        users.AuditAsync(action, entityId, page, ct);
}

[Route("api/v1/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class ContentAdminController(ContentAdminService content) : ApiControllerBase
{
    [HttpGet("settings")]
    public Task<IReadOnlyList<SettingDto>> Settings(CancellationToken ct) => content.SettingsAsync(ct);

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SaveSettingsRequest request, CancellationToken ct)
    {
        await content.SaveSettingsAsync(UserId, request.Values, ct);
        return Ok(new { message = Messages.Saved });
    }

    [HttpPut("pages/{slug}")]
    public async Task<IActionResult> SavePage(string slug, SavePageRequest request, CancellationToken ct)
    {
        await content.SavePageAsync(UserId, slug, request.Html, ct);
        return Ok(new { message = Messages.Saved });
    }

    [HttpGet("messages")]
    public Task<PagedResult<ContactMessageDto>> ListMessages([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) =>
        content.MessagesAsync(status, search, page, ct);

    [HttpGet("messages/{id:guid}")]
    public Task<ContactMessageDto> GetMessage(Guid id, CancellationToken ct) => content.OpenMessageAsync(UserId, id, ct);

    [HttpPost("messages/{id:guid}/close")]
    public async Task<IActionResult> CloseMessage(Guid id, CancellationToken ct)
    {
        await content.CloseMessageAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpGet("coupons")]
    public Task<IReadOnlyList<CouponDto>> Coupons([FromQuery] string? state, CancellationToken ct) => content.CouponsAsync(state, ct);

    [HttpPost("coupons")]
    public async Task<IActionResult> CreateCoupon(SaveCouponRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await content.SaveCouponAsync(UserId, null, request, ct) });

    [HttpPut("coupons/{id:guid}")]
    public async Task<IActionResult> UpdateCoupon(Guid id, SaveCouponRequest request, CancellationToken ct)
    {
        await content.SaveCouponAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpDelete("coupons/{id:guid}")]
    public async Task<IActionResult> DeleteCoupon(Guid id, CancellationToken ct)
    {
        await content.DeleteCouponAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpGet("banners")]
    public Task<IReadOnlyList<BannerAdminDto>> Banners(CancellationToken ct) => content.BannersAsync(ct);

    [HttpPost("banners")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> CreateBanner([FromForm] SaveBannerRequest request, IFormFile? image, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await content.SaveBannerAsync(UserId, null, request, ToUploadOrNull(image), ct) });

    [HttpPut("banners/{id:guid}")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UpdateBanner(Guid id, [FromForm] SaveBannerRequest request, IFormFile? image, CancellationToken ct)
    {
        await content.SaveBannerAsync(UserId, id, request, ToUploadOrNull(image), ct);
        return NoContent();
    }

    [HttpDelete("banners/{id:guid}")]
    public async Task<IActionResult> DeleteBanner(Guid id, CancellationToken ct)
    {
        await content.DeleteBannerAsync(UserId, id, ct);
        return NoContent();
    }
}

[Route("api/v1/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReportAdminController(ReportService reports) : ApiControllerBase
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("dashboard")]
    public Task<AdminDashboardDto> Dashboard(CancellationToken ct) => reports.DashboardAsync(ct);

    [HttpGet("reports/sales")]
    public Task<SalesReportDto> Sales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string groupBy = "day", CancellationToken ct = default) =>
        reports.SalesAsync(from, to, groupBy, ct);

    [HttpGet("reports/sales.xlsx")]
    public async Task<IActionResult> SalesExcel([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string groupBy = "day", CancellationToken ct = default)
    {
        var report = await reports.SalesAsync(from, to, groupBy, ct);
        var rows = report.Series.Select(p => (IReadOnlyList<object?>)[p.Label, p.Count, p.Value])
            .Append([("Total"), report.Series.Sum(p => p.Count), report.Series.Sum(p => p.Value)]);
        return File(ReportService.ToExcel("Sales", ["Period", "Orders", "Sales (Tk)"], rows), ExcelType, $"sales-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.xlsx");
    }

    [HttpGet("reports/users")]
    public Task<UserReportDto> Users([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => reports.UsersAsync(from, to, ct);

    [HttpGet("reports/finance")]
    public Task<FinanceReportDto> Finance(CancellationToken ct) => reports.FinanceAsync(ct);

    [HttpGet("payments")]
    public Task<PagedResult<PaymentRowDto>> Payments([FromQuery] string? method, [FromQuery] string? status, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int page = 1, CancellationToken ct = default) => reports.PaymentsAsync(method, status, from, to, page, ct);

    [HttpGet("ledger")]
    public Task<PagedResult<LedgerRowDto>> Ledger([FromQuery] Guid? orderId, [FromQuery] Guid? shopId, [FromQuery] Guid? userId, [FromQuery] string? kind,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, CancellationToken ct = default) =>
        reports.LedgerAsync(orderId, shopId, userId, kind, from, to, page, ct);

    [HttpGet("ledger/unbalanced")]
    public Task<IReadOnlyList<Guid>> Unbalanced(CancellationToken ct) => reports.UnbalancedTransactionsAsync(ct);
}

[Route("api/v1")]
[AllowAnonymous]
public sealed class SiteController(ContentAdminService content) : ApiControllerBase
{
    [HttpGet("pages/{slug}")]
    public Task<StaticPageDto> Page(string slug, CancellationToken ct) => content.PageAsync(slug, ct);

    /// <summary>Public site information for the header and footer (FR-21.2).</summary>
    [HttpGet("site")]
    public async Task<IActionResult> Site(CancellationToken ct)
    {
        var settings = await content.SettingsAsync(ct);
        return Ok(settings.Where(s => s.Group == "Site").ToDictionary(s => s.Key, s => s.Value));
    }

    [HttpPost("contact")]
    public async Task<IActionResult> Contact(ContactRequest request, CancellationToken ct)
    {
        await content.SubmitContactAsync(request, Ip, ct);
        return Ok(new { message = Messages.Saved });
    }
}
