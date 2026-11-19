using EMarket.BLL.Catalog;
using EMarket.BLL.Common;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMarket.Api.Controllers;

[Route("api/v1")]
[AllowAnonymous]
public sealed class CatalogController(CatalogQueryService catalog, CategoryService categories) : ApiControllerBase
{
    [HttpGet("home")]
    public Task<HomeDto> Home(CancellationToken ct) => catalog.HomeAsync(ct);

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => categories.TreeAsync(ct: ct);

    [HttpGet("products")]
    public Task<PagedResult<ProductListItemDto>> Search([FromQuery] ProductFilter filter, CancellationToken ct) => catalog.SearchAsync(filter, ct);

    [HttpGet("products/brands")]
    public Task<IReadOnlyList<string>> Brands([FromQuery] Guid? categoryId, CancellationToken ct) => catalog.BrandsAsync(categoryId, ct);

    [HttpGet("products/{id:guid}")]
    public Task<ProductDetailDto> Details(Guid id, CancellationToken ct) => catalog.DetailsAsync(id, ct);

    [HttpGet("shops/{slug}")]
    public Task<ShopPageDto> Shop(string slug, CancellationToken ct) => catalog.ShopAsync(slug, ct);
}

/// <summary>Public images (products, shops, banners). Identity documents and profile photos are never served here.</summary>
[Route("api/v1/media")]
[AllowAnonymous]
public sealed class MediaController(IFileStorage files) : ApiControllerBase
{
    private static readonly string[] PublicFolders = ["products/", "shops/", "banners/", "categories/"];

    [HttpGet("{**path}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Get(string path)
    {
        if (!PublicFolders.Any(path.StartsWith) || path.Contains(".."))
        {
            return NotFound();
        }

        var stream = files.Open(path);
        return stream is null ? NotFound() : File(stream, path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    }
}

[Route("api/v1/seller/products")]
[Authorize(Roles = Roles.Seller)]
public sealed class SellerProductsController(ProductService products) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<SellerProductDto>> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default) =>
        products.ListOwnAsync(UserId, status, search, page, ct);

    [HttpGet("{id:guid}")]
    public Task<SellerProductDetailDto> Get(Guid id, CancellationToken ct) => products.GetOwnAsync(UserId, id, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveProductRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await products.CreateAsync(UserId, request, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveProductRequest request, CancellationToken ct)
    {
        await products.UpdateAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        await products.SubmitAsync(UserId, id, ct);
        return Ok(new { message = Messages.ProductSubmitted });
    }

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public Task<ProductImageDto> AddImage(Guid id, IFormFile file, CancellationToken ct) => products.AddImageAsync(UserId, id, ToUpload(file), ct);

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        await products.DeleteImageAsync(UserId, id, imageId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id, CancellationToken ct)
    {
        await products.SetHiddenAsync(UserId, id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id, CancellationToken ct)
    {
        await products.SetHiddenAsync(UserId, id, false, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/stock")]
    public async Task<IActionResult> Stock(Guid id, StockRequest request, CancellationToken ct)
    {
        await products.UpdateStockAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/copy")]
    public async Task<IActionResult> Copy(Guid id, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await products.CopyAsync(UserId, id, ct) });

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/products")]
[Authorize(Roles = Roles.Admin)]
public sealed class ProductAdminController(ProductService products) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<ApprovalQueueItemDto>> List([FromQuery] string? status, [FromQuery] Guid? shopId, [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1, CancellationToken ct = default) => products.AdminListAsync(status, shopId, categoryId, page, ct);

    [HttpGet("{id:guid}")]
    public Task<SellerProductDetailDto> Get(Guid id, CancellationToken ct) => products.AdminGetAsync(id, ct);

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await products.ApproveAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await products.RejectAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await products.AdminSetHiddenAsync(UserId, id, true, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await products.AdminSetHiddenAsync(UserId, id, false, request.Reason, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await products.AdminDeleteAsync(UserId, id, request.Reason, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/categories")]
[Authorize(Roles = Roles.Admin)]
public sealed class CategoryAdminController(CategoryService categories) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> Tree(CancellationToken ct) => categories.TreeAsync(includeInactive: true, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await categories.CreateAsync(UserId, request, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveCategoryRequest request, CancellationToken ct)
    {
        await categories.UpdateAsync(UserId, id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}
