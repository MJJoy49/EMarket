using System.Globalization;
using System.Security.Cryptography;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Admin;
using EMarket.Shared.Results;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Admin;

/// <summary>FR-16: users and shops. Every block, delete, suspend and reactivate is audited (FR-16.10).</summary>
public sealed class UserAdminService(AppDbContext db, Ledger ledger, Notifier notifier, IPasswordHasher<User> hasher, IMemoryCache cache, IClock clock)
{
    public async Task<PagedResult<UserRowDto>> ListAsync(string? search, string? role, string? status, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Users.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.Contains(term) || u.Phone == term);
        }

        query = string.IsNullOrWhiteSpace(role) ? query : query.Where(u => u.Roles.Any(r => r.Name == role));
        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(u => u.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserRowDto(u.Id, u.FullName, u.Email, u.Phone, u.Roles.Select(r => r.Name!).ToList(), u.Status, u.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<UserRowDto>(items, page, pageSize, total);
    }

    /// <summary>FR-16.2: no password hash or token ever leaves this method.</summary>
    public async Task<UserDetailDto> GetAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new UserDetailDto(u.Id, u.FullName, u.Email, u.Phone, u.Roles.Select(r => r.Name!).ToList(), u.Status, u.CreatedAt,
                u.RefreshTokens.Max(t => (DateTime?)t.CreatedAt), u.Orders.Count, u.EmailConfirmed, u.Shop != null ? u.Shop.Name : null))
            .FirstOrDefaultAsync(ct) ?? throw new AppException(404, Messages.NotFound);

    public async Task BlockAsync(Guid adminId, Guid userId, string reason, CancellationToken ct = default)
    {
        var user = await TargetAsync(adminId, userId, ct);
        if (user.Status != "Active")
        {
            throw new AppException(409, Messages.Conflict);
        }

        user.Status = "Blocked";
        EndSessions(user);
        notifier.Audit(adminId, "UserBlocked", nameof(User), user.Id, "Active", reason);
        notifier.Email(user.Email, "Your EMarket account was blocked", $"Reason: {reason}. Please contact support.");
        await db.SaveChangesAsync(ct);
    }

    public async Task UnblockAsync(Guid adminId, Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (user.Status != "Blocked")
        {
            throw new AppException(409, Messages.Conflict);
        }

        user.Status = "Active";
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        notifier.Audit(adminId, "UserUnblocked", nameof(User), user.Id, "Blocked", "Active");
        notifier.Email(user.Email, "Your EMarket account was unblocked", "You can log in again.");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>BR-29: a soft delete, refused while the user has an open order or a seller balance that is not zero.</summary>
    public async Task DeleteAsync(Guid adminId, Guid userId, string reason, CancellationToken ct = default)
    {
        var user = await TargetAsync(adminId, userId, ct);
        var openOrder = await db.Orders.AnyAsync(o => (o.BuyerId == userId || o.Shop.OwnerId == userId) && OrderRules.Open.Contains(o.Status), ct);
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerId == userId, ct);
        var balance = shop is null ? 0 : (await ledger.ShopBalancesAsync(shop.Id, ct))
            .Where(b => b.Key is Ledger.SellerPending or Ledger.SellerAvailable or Ledger.SellerReserved).Sum(b => b.Value);
        if (openOrder || balance != 0)
        {
            throw new AppException(409, Messages.Conflict);
        }

        user.Status = "Deleted";
        user.IsDeleted = true;
        EndSessions(user);
        if (shop is not null)
        {
            shop.Status = "Suspended";
            shop.IsDeleted = true;
        }

        notifier.Audit(adminId, "UserDeleted", nameof(User), user.Id, newValue: reason);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-16.9: a new admin gets a random temporary password, shown once, and must change it at first login.</summary>
    public async Task<string> CreateAdminAsync(Guid adminId, CreateAdminRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
        {
            throw new AppException(409, Messages.EmailTaken);
        }

        var password = $"Em{Convert.ToHexString(RandomNumberGenerator.GetBytes(10))}a1";
        var user = new User
        {
            Id = Guid.CreateVersion7(), FullName = request.FullName.Trim(), Email = email, Phone = request.Phone, EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"), Status = "Active", MustChangePassword = true, PasswordHash = "",
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        user.Roles.Add(await db.AspNetRoles.FirstAsync(r => r.Name == Roles.Admin, ct));
        db.Users.Add(user);
        notifier.Audit(adminId, "AdminCreated", nameof(User), user.Id, newValue: email);
        await db.SaveChangesAsync(ct);
        return password;
    }

    public async Task<PagedResult<ShopRowDto>> ShopsAsync(string? status, string? search, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Shops.AsNoTracking().AsQueryable();
        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(s => s.Status == status);
        query = string.IsNullOrWhiteSpace(search) ? query : query.Where(s => s.Name.Contains(search.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new ShopRowDto(s.Id, s.Name, s.Slug, s.Owner.FullName, s.Status,
                s.Products.Where(p => p.ReviewCount > 0).Average(p => (decimal?)p.AverageRating) ?? 0,
                s.Orders.Where(o => o.Status == OrderRules.Delivered).Sum(o => (decimal?)o.ItemsTotal) ?? 0, s.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<ShopRowDto>(items, page, pageSize, total);
    }

    /// <summary>FR-16.8: a suspended shop's products disappear and no new order can be placed; placed orders go on.</summary>
    public async Task SetShopSuspendedAsync(Guid adminId, Guid shopId, bool suspend, string reason, CancellationToken ct = default)
    {
        var shop = await db.Shops.Include(s => s.Owner).FirstOrDefaultAsync(s => s.Id == shopId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (suspend == (shop.Status == "Suspended"))
        {
            throw new AppException(409, Messages.Conflict);
        }

        var old = shop.Status;
        shop.Status = suspend ? "Suspended" : "Active";
        notifier.Audit(adminId, suspend ? "ShopSuspended" : "ShopReactivated", nameof(Shop), shop.Id, old, reason);
        notifier.Notify(shop.OwnerId, suspend ? "ShopSuspended" : "ShopReactivated", suspend ? "Your shop was suspended" : "Your shop is active again", reason, "/seller");
        notifier.Email(shop.Owner.Email, suspend ? "Your EMarket shop was suspended" : "Your EMarket shop is active again", $"Reason: {reason}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditRowDto>> AuditAsync(string? action, Guid? entityId, int page, CancellationToken ct = default)
    {
        const int pageSize = 50;
        page = Math.Max(1, page);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        query = string.IsNullOrWhiteSpace(action) ? query : query.Where(a => a.Action == action);
        query = entityId is null ? query : query.Where(a => a.EntityId == entityId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditRowDto(a.CreatedAt, a.User != null ? a.User.FullName : "System", a.Action, a.EntityName, a.EntityId, a.OldValue, a.NewValue))
            .ToListAsync(ct);
        return new PagedResult<AuditRowDto>(items, page, pageSize, total);
    }

    /// <summary>FR-16.6: an admin cannot block or delete the own account or the last active admin.</summary>
    private async Task<User> TargetAsync(Guid adminId, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AppException(404, Messages.NotFound);
        var isAdmin = user.Roles.Any(r => r.Name == Roles.Admin);
        var otherActiveAdmins = await db.Users.CountAsync(u => u.Id != userId && u.Status == "Active" && u.Roles.Any(r => r.Name == Roles.Admin), ct);
        if (userId == adminId || (isAdmin && otherActiveAdmins == 0))
        {
            throw new AppException(409, Messages.Conflict);
        }

        return user;
    }

    // BR-28: all refresh tokens go, and the new stamp ends access tokens within a minute (NFR-33).
    private void EndSessions(User user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        foreach (var token in db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null))
        {
            token.RevokedAt = clock.UtcNow;
        }

        cache.Remove($"session:{user.Id}");
    }
}

/// <summary>FR-19 (coupons and banners), FR-21 (settings), FR-22 (static pages) and FR-23 (contact messages).</summary>
public sealed class ContentAdminService(AppDbContext db, IFileStorage files, Notifier notifier, IMemoryCache cache, IClock clock)
{
    public sealed record SettingRule(string Group, string ValueType, decimal? Min, decimal? Max, string Default, string Description);

    /// <summary>Every setting with its range (FR-21.3 to 21.6). Values outside the range are refused with MSG04.</summary>
    public static readonly IReadOnlyDictionary<string, SettingRule> Rules = new Dictionary<string, SettingRule>
    {
        ["siteName"] = new("Site", "Text", null, null, "EMarket", "Site name"),
        ["supportEmail"] = new("Site", "Text", null, null, "support@emarket.local", "Support email"),
        ["supportPhone"] = new("Site", "Text", null, null, "01700000000", "Support phone"),
        ["siteAddress"] = new("Site", "Text", null, null, "Dhaka, Bangladesh", "Address"),
        ["footerText"] = new("Site", "Text", null, null, "EMarket, a marketplace for Bangladesh.", "Footer text"),
        ["maintenanceMode"] = new("Site", "Int", 0, 1, "0", "Maintenance mode (1 = on)"),
        ["commissionPercent"] = new("Money", "Decimal", 0, 30, "10", "Default commission rate (%)"),
        ["deliveryFee"] = new("Money", "Decimal", 0, 1000, "60", "Default delivery fee (Tk)"),
        ["freeDeliveryAbove"] = new("Money", "Decimal", 0, 100000, "2000", "Free delivery above (Tk)"),
        ["minPayout"] = new("Money", "Decimal", 0, 100000, "500", "Minimum payout (Tk)"),
        ["codMaxAmount"] = new("Money", "Decimal", 0, 1000000, "20000", "Cash on Delivery maximum (Tk)"),
        ["unpaidOrderCancelMinutes"] = new("Time limits", "Int", 10, 120, "30", "Payment window (minutes)"),
        ["sellerConfirmHours"] = new("Time limits", "Int", 12, 168, "48", "Seller confirmation timeout (hours)"),
        ["returnWindowDays"] = new("Time limits", "Int", 1, 30, "7", "Return window and earnings waiting time (days)"),
        ["resetLinkMinutes"] = new("Time limits", "Int", 10, 120, "30", "Password reset link life (minutes)"),
        ["lowStockLimit"] = new("Stock and security", "Int", 0, 1000, "5", "Low stock alert level"),
        ["failedLoginLimit"] = new("Stock and security", "Int", 3, 20, "5", "Failed logins before lock"),
        ["lockoutMinutes"] = new("Stock and security", "Int", 1, 1440, "15", "Login lock time (minutes)"),
        ["productsPerPage"] = new("Stock and security", "Int", 4, 100, "20", "Products on one page"),
    };

    public static readonly IReadOnlyDictionary<string, string> Pages = new Dictionary<string, string>
    {
        ["about"] = "About", ["contact"] = "Contact", ["faq"] = "FAQ", ["terms"] = "Terms", ["privacy"] = "Privacy", ["seller-terms"] = "Seller Terms",
    };

    // ---------- settings ----------

    public async Task<IReadOnlyList<SettingDto>> SettingsAsync(CancellationToken ct = default)
    {
        var stored = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return Rules.Select(r => new SettingDto(r.Key, stored.GetValueOrDefault(r.Key, r.Value.Default), r.Value.ValueType, r.Value.Group,
            r.Value.Description, r.Value.Min, r.Value.Max)).ToList();
    }

    public async Task SaveSettingsAsync(Guid adminId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        foreach (var (key, raw) in values)
        {
            if (!Rules.TryGetValue(key, out var rule))
            {
                throw new AppException(400, Messages.Invalid);
            }

            var value = raw.Trim();
            if (rule.ValueType is "Int" or "Decimal")
            {
                var ok = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    && (rule.ValueType != "Int" || number == Math.Floor(number)) && number >= rule.Min && number <= rule.Max;
                if (!ok)
                {
                    throw new AppException(400, Messages.Invalid); // FR-21.6
                }
            }
            else if (value.Length is 0 or > 300)
            {
                throw new AppException(400, Messages.Invalid);
            }

            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting is null)
            {
                setting = new SystemSetting { Key = key, ValueType = rule.ValueType, Description = rule.Description, Value = rule.Default };
                db.SystemSettings.Add(setting);
            }

            if (setting.Value != value)
            {
                notifier.Audit(adminId, "SettingChanged", nameof(SystemSetting), setting.Id == Guid.Empty ? null : setting.Id, $"{key}={setting.Value}", $"{key}={value}");
                setting.Value = value;
            }

            cache.Remove($"setting:{key}"); // FR-21.7: this instance sees the change at once, others within a minute
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------- static pages ----------

    public async Task<StaticPageDto> PageAsync(string slug, CancellationToken ct = default)
    {
        if (!Pages.TryGetValue(slug, out var title))
        {
            throw new AppException(404, Messages.NotFound);
        }

        var html = await db.SystemSettings.AsNoTracking().Where(s => s.Key == $"page.{slug}").Select(s => s.Value).FirstOrDefaultAsync(ct);
        return new StaticPageDto(slug, title, html ?? $"<p>{title} will be published soon.</p>");
    }

    /// <summary>FR-22.2 and NFR-34: admin HTML is cleaned with an allow-list, so scripts never reach a page.</summary>
    public async Task SavePageAsync(Guid adminId, string slug, string html, CancellationToken ct = default)
    {
        if (!Pages.ContainsKey(slug))
        {
            throw new AppException(404, Messages.NotFound);
        }

        var clean = new HtmlSanitizer().Sanitize(html);
        var key = $"page.{slug}";
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = clean, ValueType = "Html", Description = $"{Pages[slug]} page" });
        }
        else
        {
            setting.Value = clean;
        }

        notifier.Audit(adminId, "PageChanged", nameof(SystemSetting), setting?.Id, newValue: slug);
        await db.SaveChangesAsync(ct);
    }

    // ---------- contact messages ----------

    /// <summary>FR-23.1 to 23.3: at most 3 messages an hour from one address; a filled hidden field is dropped silently.</summary>
    public async Task SubmitContactAsync(ContactRequest request, string? ip, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(request.Website))
        {
            return;
        }

        var hourAgo = clock.UtcNow.AddHours(-1);
        if (ip is not null && await db.ContactMessages.CountAsync(m => m.IpAddress == ip && m.CreatedAt > hourAgo, ct) >= 3)
        {
            throw new AppException(429, Messages.TooManyRequests); // BR-36, MSG32
        }

        db.ContactMessages.Add(new ContactMessage
        {
            Name = request.Name.Trim(), Email = request.Email.Trim(), Subject = request.Subject.Trim(), Message = request.Message.Trim(), Status = "New", IpAddress = ip,
        });
        await notifier.NotifyAdminsAsync("ContactMessage", "New contact message", request.Subject.Trim(), "/admin/messages", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ContactMessageDto>> MessagesAsync(string? status, string? search, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.ContactMessages.AsNoTracking().AsQueryable();
        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(m => m.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Name.Contains(term) || m.Email.Contains(term) || m.Subject.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new ContactMessageDto(m.Id, m.Name, m.Email, m.Subject, m.Message, m.Status, m.CreatedAt)).ToListAsync(ct);
        return new PagedResult<ContactMessageDto>(items, page, pageSize, total);
    }

    /// <summary>FR-23.5: opening a new message marks it Read; the admin can then close it.</summary>
    public async Task<ContactMessageDto> OpenMessageAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var message = await db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        if (message.Status == "New")
        {
            message.Status = "Read";
            message.HandledById = adminId;
            await db.SaveChangesAsync(ct);
        }

        return new ContactMessageDto(message.Id, message.Name, message.Email, message.Subject, message.Message, message.Status, message.CreatedAt);
    }

    public async Task CloseMessageAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var message = await db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        message.Status = "Closed";
        message.HandledById = adminId;
        await db.SaveChangesAsync(ct);
    }

    // ---------- coupons ----------

    public async Task<IReadOnlyList<CouponDto>> CouponsAsync(string? state, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var all = await db.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return all.Select(c => new CouponDto(c.Id, c.Code, c.DiscountType, c.DiscountValue, c.MaxDiscount, c.MinOrderAmount, c.StartsAt, c.ExpiresAt,
                c.UsageLimit, c.PerUserLimit, c.UsedCount, c.IsActive,
                !c.IsActive ? "Disabled" : c.ExpiresAt <= now ? "Expired" : c.StartsAt > now ? "Scheduled" : c.UsageLimit <= c.UsedCount ? "Used up" : "Active"))
            .Where(c => string.IsNullOrWhiteSpace(state) || c.State == state).ToList();
    }

    public async Task<Guid> SaveCouponAsync(Guid adminId, Guid? id, SaveCouponRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code.ToUpper() == code && c.Id != id, ct))
        {
            throw new AppException(409, Messages.Conflict); // FR-19.1: unique without regard to case
        }

        var coupon = id is null ? new Coupon { Id = Guid.CreateVersion7() } : await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        if (id is null)
        {
            db.Coupons.Add(coupon);
        }

        coupon.Code = code;
        coupon.DiscountType = request.DiscountType;
        coupon.DiscountValue = request.DiscountValue;
        coupon.MaxDiscount = request.DiscountType == "Percent" ? request.MaxDiscount : null;
        coupon.MinOrderAmount = request.MinOrderAmount;
        coupon.StartsAt = request.StartsAt;
        coupon.ExpiresAt = request.ExpiresAt;
        coupon.UsageLimit = request.UsageLimit;
        coupon.PerUserLimit = request.PerUserLimit;
        coupon.IsActive = request.IsActive;
        notifier.Audit(adminId, id is null ? "CouponCreated" : "CouponChanged", nameof(Coupon), coupon.Id, newValue: code);
        await db.SaveChangesAsync(ct);
        return coupon.Id;
    }

    /// <summary>FR-19.4: a coupon that was used cannot be deleted, only disabled.</summary>
    public async Task DeleteCouponAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        if (coupon.UsedCount > 0 || await db.CouponRedemptions.AnyAsync(r => r.CouponId == id, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        db.Coupons.Remove(coupon);
        notifier.Audit(adminId, "CouponDeleted", nameof(Coupon), id, coupon.Code);
        await db.SaveChangesAsync(ct);
    }

    // ---------- banners ----------

    public async Task<IReadOnlyList<BannerAdminDto>> BannersAsync(CancellationToken ct = default) =>
        await db.Banners.AsNoTracking().OrderBy(b => b.SortOrder)
            .Select(b => new BannerAdminDto(b.Id, b.Title, "/api/v1/media/" + b.ImageUrl, b.LinkUrl, b.SortOrder, b.StartsAt, b.EndsAt, b.IsActive)).ToListAsync(ct);

    public async Task<Guid> SaveBannerAsync(Guid adminId, Guid? id, SaveBannerRequest request, UploadFile? image, CancellationToken ct = default)
    {
        if (request.StartsAt is { } start && request.EndsAt is { } end && end <= start)
        {
            throw new AppException(400, Messages.Invalid);
        }

        var banner = id is null ? new Banner { Id = Guid.CreateVersion7() } : await db.Banners.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        if (id is null)
        {
            // FR-19.7: a banner without an image is refused.
            banner.ImageUrl = image is null ? throw new AppException(400, Messages.BadFile) : await files.SaveImageAsync(image, "banners", ct);
            db.Banners.Add(banner);
        }
        else if (image is not null)
        {
            banner.ImageUrl = await files.SaveImageAsync(image, "banners", ct);
        }

        banner.Title = request.Title.Trim();
        banner.LinkUrl = request.LinkUrl;
        banner.SortOrder = request.SortOrder;
        banner.StartsAt = request.StartsAt;
        banner.EndsAt = request.EndsAt;
        banner.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        cache.Remove("catalog:home");
        return banner.Id;
    }

    public async Task DeleteBannerAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var banner = await db.Banners.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new AppException(404, Messages.NotFound);
        db.Banners.Remove(banner);
        await db.SaveChangesAsync(ct);
        files.Delete(banner.ImageUrl);
        cache.Remove("catalog:home");
    }
}
