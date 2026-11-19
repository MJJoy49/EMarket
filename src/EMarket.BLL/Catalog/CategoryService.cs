using System.Text.RegularExpressions;
using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Catalog;

public sealed partial class CategoryService(AppDbContext db, IMemoryCache cache, Notifier notifier)
{
    private const string TreeKey = "catalog:category-tree";

    /// <summary>The whole tree, cached for 60 seconds (FR-5.10). Inactive branches are left out unless asked for.</summary>
    public async Task<IReadOnlyList<CategoryDto>> TreeAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var all = await cache.GetOrCreateAsync(TreeKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        }) ?? [];

        IReadOnlyList<CategoryDto> Build(Guid? parentId) => all
            .Where(c => c.ParentId == parentId && (includeInactive || c.IsActive))
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId, c.SortOrder, c.IsActive, Build(c.Id)))
            .ToList();

        return Build(null);
    }

    public async Task<Guid> CreateAsync(Guid adminId, SaveCategoryRequest request, CancellationToken ct = default)
    {
        await CheckAsync(null, request, ct);
        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            Slug = await UniqueSlugAsync(request.Name, ct),
        };
        db.Categories.Add(category);
        notifier.Audit(adminId, "CategoryCreated", nameof(Category), category.Id, newValue: category.Name);
        await SaveAndRefreshAsync(ct);
        return category.Id;
    }

    public async Task UpdateAsync(Guid adminId, Guid id, SaveCategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        await CheckAsync(id, request, ct);
        var old = $"{category.Name}|{category.IsActive}|{category.SortOrder}";
        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive; // FR-17.7: a disabled category and its products drop out of browsing.
        notifier.Audit(adminId, "CategoryUpdated", nameof(Category), id, old, $"{category.Name}|{category.IsActive}|{category.SortOrder}");
        await SaveAndRefreshAsync(ct);
    }

    /// <summary>BR-30: only an empty category with no children can be deleted; otherwise disable it.</summary>
    public async Task DeleteAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct) || await db.Products.IgnoreQueryFilters().AnyAsync(p => p.CategoryId == id, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        db.Categories.Remove(category);
        notifier.Audit(adminId, "CategoryDeleted", nameof(Category), id, category.Name);
        await SaveAndRefreshAsync(ct);
    }

    /// <summary>A product must sit in an active leaf category (SRS 4.7).</summary>
    public async Task<Category> ActiveLeafAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories.Include(c => c.Parent).FirstOrDefaultAsync(c => c.Id == id, ct);
        var isLeaf = category is not null && !await db.Categories.AnyAsync(c => c.ParentId == id, ct);
        return isLeaf && category!.IsActive && category.Parent?.IsActive != false ? category : throw new AppException(400, Messages.Invalid);
    }

    private async Task CheckAsync(Guid? id, SaveCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim().ToLower();
        if (request.ParentId is { } parentId)
        {
            // Two levels at most (FR-17.6): the parent must be a main category, and a category with children cannot move under another.
            var parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);
            var hasChildren = id is not null && await db.Categories.AnyAsync(c => c.ParentId == id, ct);
            if (parent is null || parent.ParentId is not null || parentId == id || hasChildren)
            {
                throw new AppException(400, Messages.Invalid);
            }
        }

        if (await db.Categories.AnyAsync(c => c.ParentId == request.ParentId && c.Id != id && c.Name.ToLower() == name, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    private async Task SaveAndRefreshAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        cache.Remove(TreeKey); // FR-17.10: buyers see the change at once on this instance.
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = SlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        baseSlug = string.IsNullOrEmpty(baseSlug) ? "category" : baseSlug;
        var slug = baseSlug;
        for (var i = 2; await db.Categories.AnyAsync(c => c.Slug == slug, ct); i++)
        {
            slug = $"{baseSlug}-{i}";
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}
