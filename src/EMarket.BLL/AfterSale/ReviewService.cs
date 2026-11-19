using EMarket.BLL.Common;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.AfterSale;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.AfterSale;

/// <summary>FR-12: reviews of delivered items, one public seller reply, and admin moderation.</summary>
public sealed class ReviewService(AppDbContext db, Notifier notifier, IClock clock)
{
    private const int EditDays = 7;

    public async Task<Guid> CreateAsync(Guid buyerId, Guid orderItemId, ReviewInput input, CancellationToken ct = default)
    {
        var item = await db.OrderItems.Include(i => i.Order).ThenInclude(o => o.Shop).Include(i => i.Review)
            .FirstOrDefaultAsync(i => i.Id == orderItemId && i.Order.BuyerId == buyerId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (item.Order.Status != OrderRules.Delivered)
        {
            throw new AppException(403, Messages.NotAllowed); // BR-18, MSG29
        }

        if (item.Review is not null)
        {
            throw new AppException(409, Messages.Conflict);
        }

        var review = new Review
        {
            Id = Guid.CreateVersion7(), ProductId = item.ProductId, OrderItemId = item.Id, BuyerId = buyerId, Rating = input.Rating,
            Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(), Status = "Visible",
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(item.ProductId, ct);
        notifier.Notify(item.Order.Shop.OwnerId, "ReviewPosted", "New review", $"{item.ProductName}: {input.Rating} stars.", "/seller/reviews");
        await db.SaveChangesAsync(ct);
        return review.Id;
    }

    public async Task UpdateAsync(Guid buyerId, Guid reviewId, ReviewInput input, CancellationToken ct = default)
    {
        var review = await OwnEditableAsync(buyerId, reviewId, ct);
        review.Rating = input.Rating;
        review.Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(review.ProductId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid buyerId, Guid reviewId, CancellationToken ct = default)
    {
        var review = await OwnEditableAsync(buyerId, reviewId, ct);
        db.Reviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(review.ProductId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-12.5: visible reviews, newest first, 10 a page, with a masked name and a verified-purchase mark.</summary>
    public async Task<PagedResult<ReviewDto>> ForProductAsync(Guid productId, int page, CancellationToken ct = default)
    {
        const int pageSize = 10;
        page = Math.Max(1, page);
        var query = db.Reviews.AsNoTracking().Where(r => r.ProductId == productId && r.Status == "Visible");
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r, Name = r.Buyer.FullName, Product = r.Product.Name }).ToListAsync(ct);
        return new PagedResult<ReviewDto>(rows.Select(x => ToDto(x.r, x.Product, Mask(x.Name), 0)).ToList(), page, pageSize, total);
    }

    public async Task<(PagedResult<ReviewDto> Page, decimal Average, int Count)> ForSellerAsync(Guid sellerId, Guid? productId, int? stars, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Reviews.AsNoTracking().Where(r => r.Product.Shop.OwnerId == sellerId);
        var average = await query.Where(r => r.Status == "Visible").AverageAsync(r => (decimal?)r.Rating, ct) ?? 0;
        var count = await query.CountAsync(r => r.Status == "Visible", ct);
        query = productId is null ? query : query.Where(r => r.ProductId == productId);
        query = stars is null ? query : query.Where(r => r.Rating == stars);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r, Name = r.Buyer.FullName, Product = r.Product.Name }).ToListAsync(ct);
        return (new PagedResult<ReviewDto>(rows.Select(x => ToDto(x.r, x.Product, Mask(x.Name), 0)).ToList(), page, pageSize, total), Math.Round(average, 1), count);
    }

    /// <summary>FR-12.6: one public reply per review.</summary>
    public async Task ReplyAsync(Guid sellerId, Guid reviewId, string reply, CancellationToken ct = default)
    {
        var review = await db.Reviews.Include(r => r.Product).ThenInclude(p => p.Shop).FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null || review.Product.Shop.OwnerId != sellerId)
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (review.SellerReply is not null)
        {
            throw new AppException(409, Messages.Conflict);
        }

        review.SellerReply = reply.Trim();
        review.SellerRepliedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-12.8: a user reports a review once.</summary>
    public async Task ReportAsync(Guid userId, Guid reviewId, string reason, CancellationToken ct = default)
    {
        if (!await db.Reviews.AnyAsync(r => r.Id == reviewId, ct))
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (await db.ReviewReports.AnyAsync(r => r.ReviewId == reviewId && r.ReporterId == userId, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        db.ReviewReports.Add(new ReviewReport { ReviewId = reviewId, ReporterId = userId, Reason = reason.Trim(), Status = "Open" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ReviewDto>> AdminListAsync(bool reportedOnly, string? status, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Reviews.AsNoTracking().AsQueryable();
        query = reportedOnly ? query.Where(r => r.ReviewReports.Any(x => x.Status == "Open")) : query;
        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(r => r.Status == status);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r, Name = r.Buyer.FullName, Product = r.Product.Name, Reports = r.ReviewReports.Count(x => x.Status == "Open") }).ToListAsync(ct);
        return new PagedResult<ReviewDto>(rows.Select(x => ToDto(x.r, x.Product, x.Name, x.Reports)).ToList(), page, pageSize, total);
    }

    /// <summary>FR-12.7: a hidden review is not shown and not counted; the change is audited (BR-41).</summary>
    public async Task SetHiddenAsync(Guid adminId, Guid reviewId, bool hidden, string reason, CancellationToken ct = default)
    {
        var review = await db.Reviews.Include(r => r.ReviewReports).FirstOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw new AppException(404, Messages.NotFound);
        review.Status = hidden ? "Hidden" : "Visible";
        foreach (var report in review.ReviewReports.Where(r => r.Status == "Open"))
        {
            report.Status = hidden ? "Upheld" : "Dismissed";
            report.DecidedById = adminId;
        }

        notifier.Audit(adminId, hidden ? "ReviewHidden" : "ReviewShown", nameof(Review), review.Id, newValue: reason);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(review.ProductId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-12.4: the average counts visible reviews only.</summary>
    private async Task RecalculateAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == productId, ct);
        var visible = db.Reviews.Where(r => r.ProductId == productId && r.Status == "Visible");
        product.ReviewCount = await visible.CountAsync(ct);
        product.AverageRating = product.ReviewCount == 0 ? 0 : Math.Round((decimal)await visible.AverageAsync(r => r.Rating, ct), 2);
    }

    private async Task<Review> OwnEditableAsync(Guid buyerId, Guid reviewId, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId && r.BuyerId == buyerId, ct) ?? throw new AppException(404, Messages.NotFound);
        return review.CreatedAt.AddDays(EditDays) >= clock.UtcNow ? review : throw new AppException(409, Messages.Conflict); // FR-12.3
    }

    /// <summary>"Abdul Karim" becomes "A*** K***" (FR-12.5).</summary>
    public static string Mask(string fullName) =>
        string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => $"{part[0]}***"));

    private static ReviewDto ToDto(Review r, string product, string name, int reports) =>
        new(r.Id, r.ProductId, product, name, r.Rating, r.Comment, r.CreatedAt, true, r.Status, r.SellerReply, r.SellerRepliedAt, reports);
}

/// <summary>FR-14: each user's in-app notifications. A user only ever reads their own (FR-14.7).</summary>
public sealed class NotificationService(AppDbContext db)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(Guid userId, bool unreadOnly, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.LinkUrl, n.IsRead, n.CreatedAt)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkReadAsync(Guid userId, Guid? notificationId, CancellationToken ct = default)
    {
        var query = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (notificationId is { } id)
        {
            var one = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct) ?? throw new AppException(404, Messages.NotFound);
            one.IsRead = true;
        }
        else
        {
            (await query.ToListAsync(ct)).ForEach(n => n.IsRead = true);
        }

        await db.SaveChangesAsync(ct);
    }
}
