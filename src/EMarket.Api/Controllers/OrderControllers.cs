using EMarket.BLL.Orders;
using EMarket.BLL.Payments;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Dtos.Orders;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1/orders")]
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
public sealed class OrdersController(OrderService orders, PaymentService payments) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default) =>
        orders.BuyerListAsync(UserId, status, page, ct);

    [HttpGet("{id:guid}")]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken ct) => orders.BuyerDetailAsync(UserId, id, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelRequest request, CancellationToken ct)
    {
        await orders.CancelByBuyerAsync(UserId, id, request.Reason, ct);
        return Ok(new { message = Messages.OrderCancelled });
    }

    /// <summary>Pay now for a whole checkout group (FR-10.3).</summary>
    [HttpPost("groups/{groupId:guid}/pay")]
    public Task<PaymentStartDto> Pay(Guid groupId, CancellationToken ct) => payments.StartAsync(UserId, groupId, ct);
}

[Route("api/v1/seller/orders")]
[Authorize(Roles = Roles.Seller)]
public sealed class SellerOrdersController(OrderService orders) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, CancellationToken ct = default) => orders.SellerListAsync(UserId, status, from, to, page, ct);

    [HttpGet("{id:guid}")]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken ct) => orders.SellerDetailAsync(UserId, id, ct);

    [HttpPost("{id:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid id, CancellationToken ct) => Move(id, OrderRules.Confirmed, null, ct);

    [HttpPost("{id:guid}/process")]
    public Task<IActionResult> Process(Guid id, CancellationToken ct) => Move(id, OrderRules.Processing, null, ct);

    [HttpPost("{id:guid}/ship")]
    public async Task<IActionResult> Ship(Guid id, ShipRequest request, CancellationToken ct)
    {
        await orders.ShipAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deliver")]
    public Task<IActionResult> Deliver(Guid id, CancellationToken ct) => Move(id, OrderRules.Delivered, null, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancelRequest request, CancellationToken ct) => Move(id, OrderRules.Cancelled, request.Reason, ct);

    [HttpPut("{id:guid}/tracking")]
    public async Task<IActionResult> Tracking(Guid id, ShipRequest request, CancellationToken ct)
    {
        await orders.SetTrackingAsync(UserId, id, request, ct);
        return NoContent();
    }

    private async Task<IActionResult> Move(Guid id, string to, string? note, CancellationToken ct)
    {
        await orders.SellerMoveAsync(UserId, id, to, note, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/orders")]
[Authorize(Roles = Roles.Admin)]
public sealed class OrderAdminController(OrderService orders, RefundService refunds) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] Guid? shopId,
        [FromQuery] string? paymentStatus, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, CancellationToken ct = default) =>
        orders.AdminListAsync(status, search, shopId, paymentStatus, from, to, page, ct);

    [HttpGet("{id:guid}")]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken ct) => orders.AdminDetailAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await orders.CancelByAdminAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/close-delivered")]
    public async Task<IActionResult> CloseDelivered(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await orders.CloseStuckAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("refunds/{refundId:guid}/retry")]
    public async Task<IActionResult> RetryRefund(Guid refundId, CancellationToken ct)
    {
        await refunds.RetryAsync(UserId, refundId, ct);
        return NoContent();
    }
}

/// <summary>Gateway callbacks. They only name a transaction; the result is always read back from the gateway (FR-9.5).</summary>
[Route("api/v1/payments")]
[AllowAnonymous]
public sealed class PaymentCallbackController(PaymentService payments, GatewayOptions options, IConfiguration config) : ApiControllerBase
{
    [HttpPost("gateway/{kind}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Callback(string kind, [FromForm(Name = "tran_id")] string? transactionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionId) || kind is not ("success" or "fail" or "cancel" or "ipn"))
        {
            return BadRequest();
        }

        var status = await payments.SettleAsync(transactionId, ct);
        return kind == "ipn" ? Ok(new { status }) : Redirect(ResultUrl(transactionId, status));
    }

    /// <summary>The local stand-in for the gateway page (Gateway:Mode = Fake). It does not exist in SslCommerz mode.</summary>
    [HttpGet("fake/{transactionId}")]
    public IActionResult FakePage(string transactionId)
    {
        if (options.Mode != "Fake")
        {
            return NotFound();
        }

        var safe = System.Net.WebUtility.HtmlEncode(transactionId);
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Test payment</title><style>body{font-family:system-ui,sans-serif;max-width:420px;margin:48px auto;padding:0 16px;color:#1f2937}
            button{display:block;width:100%;min-height:44px;margin:8px 0;font-size:16px;border-radius:8px;border:1px solid #9ca3af;background:#fff;cursor:pointer}
            button:focus-visible{outline:3px solid #2563eb;outline-offset:2px}</style></head><body>
            <h1>Test payment</h1><p>This page stands in for the payment gateway on a development machine. No money moves.</p>
            <p>Transaction: <code>{{safe}}</code></p>
            <form method="post" action="/api/v1/payments/fake/{{safe}}">
            <button name="result" value="VALID">Pay</button><button name="result" value="FAILED">Fail</button><button name="result" value="CANCELLED">Cancel</button>
            </form></body></html>
            """;
        return Content(html, "text/html");
    }

    [HttpPost("fake/{transactionId}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> FakeDecide(string transactionId, [FromForm] string result, CancellationToken ct)
    {
        if (options.Mode != "Fake" || result is not ("VALID" or "FAILED" or "CANCELLED"))
        {
            return NotFound();
        }

        FakeGateway.Decide(transactionId, result);
        var status = await payments.SettleAsync(transactionId, ct);
        return Redirect(ResultUrl(transactionId, status));
    }

    private string ResultUrl(string transactionId, string status) =>
        $"{(config["Web:BaseUrl"] ?? "https://localhost:7002").TrimEnd('/')}/checkout/result?tran={Uri.EscapeDataString(transactionId)}&status={Uri.EscapeDataString(status)}";
}
