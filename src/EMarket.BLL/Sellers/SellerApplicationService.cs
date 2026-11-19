using System.Text.RegularExpressions;
using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Sellers;

public sealed record ApplicationFiles(UploadFile NidFront, UploadFile NidBack, UploadFile? TradeLicence);

public sealed partial class SellerApplicationService(
    AppDbContext db, IFileStorage files, ISecretProtector secrets, Notifier notifier, IClock clock)
{
    private const int ReapplyDays = 7;

    public async Task<Guid> ApplyAsync(Guid userId, SellerApplicationRequest request, ApplicationFiles upload, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        var roles = user.Roles.Select(r => r.Name).ToHashSet();
        if (roles.Contains(Roles.Seller) || roles.Contains(Roles.Admin))
        {
            throw new AppException(403, Messages.NotAllowed);
        }

        if (!user.EmailConfirmed)
        {
            throw new AppException(403, Messages.VerifyEmailFirst);
        }

        await CheckCanApplyAsync(userId, request.ShopName, ct);
        var account = PayoutAccount(request);

        var application = new SellerApplication
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ShopName = request.ShopName.Trim(),
            ShopDescription = request.ShopDescription?.Trim() ?? "",
            BusinessType = request.BusinessType,
            NidNumber = secrets.Protect(request.NidNumber),
            PayoutMethod = request.PayoutMethod,
            PayoutAccount = secrets.Protect(account),
            Status = "Pending",
            NidFrontPath = await files.SaveImageAsync(upload.NidFront, "nid", ct),
            NidBackPath = await files.SaveImageAsync(upload.NidBack, "nid", ct),
            TradeLicencePath = upload.TradeLicence is null ? null : await files.SaveImageAsync(upload.TradeLicence, "nid", ct),
        };
        db.SellerApplications.Add(application);
        notifier.Notify(userId, "SellerApplicationSubmitted", "Seller application submitted", Messages.ApplicationSent, "/seller/status");
        notifier.Audit(userId, "SellerTermsAccepted", nameof(SellerApplication), application.Id);
        await notifier.NotifyAdminsAsync("SellerApplicationSubmitted", "New seller application",
            $"{user.FullName} applied to open {application.ShopName}.", $"/admin/applications/{application.Id}", ct);
        await db.SaveChangesAsync(ct);
        return application.Id;
    }

    public async Task<SellerApplicationDto?> LatestForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var application = await Query().Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        return application is null ? null : ToDto(application, revealSecrets: false);
    }

    public async Task<PagedResult<SellerApplicationDto>> ListAsync(string? status, string? search, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = Query();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.ShopName.Contains(term) || a.User.FullName.Contains(term) || a.User.Email.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<SellerApplicationDto>(items.Select(a => ToDto(a, revealSecrets: false)).ToList(), page, pageSize, total);
    }

    /// <summary>Admin view with the decrypted NID; each opening is logged (BR-43).</summary>
    public async Task<SellerApplicationDto> GetForAdminAsync(Guid adminId, Guid applicationId, CancellationToken ct = default)
    {
        var application = await Query().FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new AppException(404, Messages.NotFound);
        notifier.Audit(adminId, "NidViewed", nameof(SellerApplication), applicationId);
        await db.SaveChangesAsync(ct);
        return ToDto(application, revealSecrets: true);
    }

    public async Task<Stream> OpenDocumentAsync(Guid adminId, Guid applicationId, string document, CancellationToken ct = default)
    {
        var application = await db.SellerApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        var path = document switch
        {
            "nid-front" => application.NidFrontPath,
            "nid-back" => application.NidBackPath,
            "trade-licence" => application.TradeLicencePath,
            _ => null,
        };
        var stream = (path is null ? null : files.Open(path)) ?? throw new AppException(404, Messages.NotFound);
        notifier.Audit(adminId, "DocumentOpened", nameof(SellerApplication), applicationId, newValue: document);
        await db.SaveChangesAsync(ct);
        return stream;
    }

    public async Task DecideAsync(Guid adminId, Guid applicationId, ReviewDecisionRequest decision, CancellationToken ct = default)
    {
        var application = await db.SellerApplications.Include(a => a.User).ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (application.Status != "Pending")
        {
            throw new AppException(409, Messages.Conflict);
        }

        application.ReviewedById = adminId;
        application.ReviewedAt = clock.UtcNow;
        var user = application.User;
        if (decision.Approve)
        {
            await ApproveAsync(application, user, ct);
        }
        else
        {
            var reason = decision.Reason?.Trim();
            if (reason is null || reason.Length is < 10 or > 500)
            {
                throw new AppException(400, Messages.Invalid);
            }

            application.Status = "Rejected";
            application.RejectionReason = reason;
            notifier.Notify(user.Id, "SellerRejected", "Seller application rejected", reason, "/seller/status");
            notifier.Email(user.Email, "Your EMarket seller application", $"Your application was rejected. Reason: {reason}. You may apply again after {ReapplyDays} days.");
        }

        notifier.Audit(adminId, decision.Approve ? "SellerApproved" : "SellerRejected", nameof(SellerApplication), application.Id, newValue: application.RejectionReason);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApproveAsync(SellerApplication application, User user, CancellationToken ct)
    {
        if (await ShopNameTakenAsync(application.ShopName, application.Id, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        application.Status = "Approved";
        user.Roles.Add(await db.AspNetRoles.FirstAsync(r => r.Name == Roles.Seller, ct));
        var slug = await UniqueSlugAsync(application.ShopName, ct);
        db.Shops.Add(new Shop
        {
            Id = Guid.CreateVersion7(),
            OwnerId = user.Id,
            Name = application.ShopName,
            Slug = slug,
            Description = application.ShopDescription,
            Status = "Active",
            InsideDhakaFee = 60,
            OutsideDhakaFee = 60,
            FreeDeliveryAbove = 2000,
            PayoutMethod = application.PayoutMethod,
            PayoutAccount = application.PayoutAccount,
        });
        notifier.Notify(user.Id, "SellerApproved", "You are now a seller", string.Format(Messages.ApplicationResult, "approved"), "/seller");
        notifier.Email(user.Email, "Your EMarket seller application", "Your application was approved. Log in again to open your seller dashboard.");
    }

    private async Task CheckCanApplyAsync(Guid userId, string shopName, CancellationToken ct)
    {
        var latest = await db.SellerApplications.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var tooSoon = latest is { Status: "Rejected", ReviewedAt: { } reviewedAt } && reviewedAt.AddDays(ReapplyDays) > clock.UtcNow;
        if (latest?.Status == "Pending" || tooSoon || await ShopNameTakenAsync(shopName, null, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    // Shop names are unique without regard to letter case (BR-3), among shops and open applications.
    private async Task<bool> ShopNameTakenAsync(string name, Guid? exceptApplicationId, CancellationToken ct)
    {
        var lower = name.Trim().ToLower();
        return await db.Shops.IgnoreQueryFilters().AnyAsync(s => s.Name.ToLower() == lower, ct)
            || await db.SellerApplications.AnyAsync(a => a.Status == "Pending" && a.Id != exceptApplicationId && a.ShopName.ToLower() == lower, ct);
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = SlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        baseSlug = string.IsNullOrEmpty(baseSlug) ? "shop" : baseSlug;
        var slug = baseSlug;
        for (var i = 2; await db.Shops.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug, ct); i++)
        {
            slug = $"{baseSlug}-{i}";
        }

        return slug;
    }

    private static string PayoutAccount(SellerApplicationRequest request)
    {
        if (request.PayoutMethod == "BankTransfer")
        {
            string?[] needed = [request.BankName, request.BankBranch, request.RoutingNumber, request.AccountName, request.AccountNumber];
            return needed.Any(string.IsNullOrWhiteSpace)
                ? throw new AppException(400, Messages.Required)
                : $"{request.BankName}|{request.BankBranch}|{request.RoutingNumber}|{request.AccountName}|{request.AccountNumber}";
        }

        return string.IsNullOrWhiteSpace(request.WalletNumber) ? throw new AppException(400, Messages.Required) : request.WalletNumber;
    }

    private IQueryable<SellerApplication> Query() => db.SellerApplications.AsNoTracking().Include(a => a.User);

    private SellerApplicationDto ToDto(SellerApplication a, bool revealSecrets) => new(
        a.Id, a.UserId, a.User.FullName, a.User.Email, a.ShopName, a.ShopDescription, a.BusinessType, a.Status, a.PayoutMethod,
        a.RejectionReason, a.CreatedAt, a.ReviewedAt, a.TradeLicencePath is not null,
        revealSecrets ? secrets.Unprotect(a.NidNumber) : null,
        revealSecrets ? secrets.Unprotect(a.PayoutAccount) : null);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}
