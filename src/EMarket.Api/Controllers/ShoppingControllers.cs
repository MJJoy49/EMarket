using EMarket.BLL.Shopping;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Cart;
using EMarket.Shared.Dtos.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

/// <summary>Buying is for buyers and sellers; an admin neither buys nor sells (BR-26).</summary>
[Route("api/v1/cart")]
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
public sealed class CartController(CartService carts) : ApiControllerBase
{
    [HttpGet]
    public Task<CartDto> Get(CancellationToken ct) => carts.GetAsync(UserId, ct);

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken ct) => Ok(new { count = await carts.CountAsync(UserId, ct) });

    [HttpPost("items")]
    public async Task<IActionResult> Add(AddToCartRequest request, CancellationToken ct)
    {
        await carts.AddAsync(UserId, request, ct);
        return Ok(new { message = Messages.AddedToCart });
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> Quantity(Guid id, QuantityRequest request, CancellationToken ct)
    {
        await carts.SetQuantityAsync(UserId, id, request.Quantity, ct);
        return NoContent();
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await carts.RemoveAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("coupon")]
    public async Task<IActionResult> ApplyCoupon(CouponRequest request, CancellationToken ct)
    {
        var discount = await carts.ApplyCouponAsync(UserId, request.Code, ct);
        return Ok(new { discount, message = string.Format(Messages.CouponApplied, discount.ToString("N2")) });
    }

    [HttpDelete("coupon")]
    public async Task<IActionResult> RemoveCoupon(CancellationToken ct)
    {
        await carts.RemoveCouponAsync(UserId, ct);
        return NoContent();
    }
}

[Route("api/v1/wishlist")]
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
public sealed class WishlistController(CartService carts) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WishlistItemDto>> Get(CancellationToken ct) => carts.WishlistAsync(UserId, ct);

    /// <summary>FR-7.1: the heart button adds the product, or removes it when it is already saved.</summary>
    [HttpPost("{productId:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid productId, CancellationToken ct) =>
        Ok(new { saved = await carts.ToggleWishlistAsync(UserId, productId, ct) });
}

[Route("api/v1/checkout")]
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
public sealed class CheckoutController(CheckoutService checkout) : ApiControllerBase
{
    [HttpPost("summary")]
    public Task<CheckoutSummaryDto> Summary(CheckoutRequest request, CancellationToken ct) => checkout.SummaryAsync(UserId, request, ct);

    /// <summary>Needs an Idempotency-Key header; a repeat with the same key returns the first result (NFR-2).</summary>
    [HttpPost]
    public async Task<IActionResult> Place(CheckoutRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var result = await checkout.PlaceAsync(UserId, request, idempotencyKey ?? "", ct);
        return result.Repeated ? Ok(new { message = Messages.AlreadyDone, result }) : StatusCode(StatusCodes.Status201Created, result);
    }
}
