using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Profile;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Profile;

public sealed class ProfileService(AppDbContext db, IFileStorage files, Notifier notifier, IDataProtectionProvider protection, IClock clock)
{
    private readonly ITimeLimitedDataProtector emailChange =
        protection.CreateProtector("EMarket.EmailChange.v1").ToTimeLimitedDataProtector();

    public async Task<ProfileDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        var credit = await db.LedgerEntries.Where(l => l.UserId == userId && l.Account == "StoreCredit").SumAsync(l => (decimal?)l.Amount, ct) ?? 0;
        return new ProfileDto(user.Id, user.FullName, user.Email, user.Phone, user.Gender, user.BirthDate, user.PhotoUrl,
            user.EmailConfirmed, user.Roles.Select(r => r.Name!).ToList(), credit);
    }

    /// <summary>FR-24.7: the store credit history is the buyer's own ledger lines.</summary>
    public async Task<IReadOnlyList<CreditLineDto>> CreditHistoryAsync(Guid userId, CancellationToken ct = default) =>
        await db.LedgerEntries.AsNoTracking().Where(l => l.UserId == userId && l.Account == "StoreCredit")
            .OrderByDescending(l => l.CreatedAt).Take(100)
            .Select(l => new CreditLineDto(l.CreatedAt, l.Kind, l.Amount, l.Order != null ? l.Order.OrderNumber : null)).ToListAsync(ct);

    public async Task<bool> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (request.BirthDate > DateOnly.FromDateTime(clock.UtcNow))
        {
            throw new AppException(400, Messages.Invalid);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AppException(404, Messages.NotFound);
        user.FullName = request.FullName.Trim();
        user.Phone = request.Phone.Trim();
        user.Gender = request.Gender;
        user.BirthDate = request.BirthDate;

        // FR-2.4: a new email becomes the login only after its link is used; the old one keeps working until then.
        var newEmail = request.Email.Trim().ToLowerInvariant();
        var emailChangeStarted = newEmail != user.Email;
        if (emailChangeStarted)
        {
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == newEmail, ct))
            {
                throw new AppException(409, Messages.EmailTaken);
            }

            var token = emailChange.Protect($"{user.Id}|{newEmail}|{user.SecurityStamp}", TimeSpan.FromHours(24));
            notifier.Email(newEmail, "Confirm your new EMarket email", $"Open this link within 24 hours: /confirm-email-change?token={Uri.EscapeDataString(token)}");
        }

        await db.SaveChangesAsync(ct);
        return emailChangeStarted;
    }

    public async Task ConfirmEmailChangeAsync(string token, CancellationToken ct = default)
    {
        string[] parts;
        try
        {
            parts = emailChange.Unprotect(token).Split('|');
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AppException(400, Messages.BadLink);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Guid.Parse(parts[0]), ct);
        // The stamp changes when the link is used, so each link works once.
        if (user is null || user.SecurityStamp != parts[2] || await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == parts[1], ct))
        {
            throw new AppException(400, Messages.BadLink);
        }

        var oldEmail = user.Email;
        user.Email = parts[1];
        user.EmailConfirmed = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        notifier.Email(oldEmail, "Your EMarket email was changed", $"Your login email is now {user.Email}. If this was not you, contact support.");
        notifier.Audit(user.Id, "EmailChanged", nameof(User), user.Id, oldEmail, user.Email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> SetPhotoAsync(Guid userId, UploadFile file, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AppException(404, Messages.NotFound);
        var path = await files.SaveImageAsync(file, "profile", ct);
        if (user.PhotoUrl is not null)
        {
            files.Delete(user.PhotoUrl);
        }

        user.PhotoUrl = path;
        await db.SaveChangesAsync(ct);
        return path;
    }

    public async Task<IReadOnlyList<LocationDto>> LocationsAsync(Guid? parentId, CancellationToken ct = default) =>
        await db.Locations.AsNoTracking().Where(l => l.ParentId == parentId).OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.Level, l.IsInsideDhaka)).ToListAsync(ct);
}

public sealed class AddressService(AppDbContext db)
{
    public async Task<IReadOnlyList<AddressDto>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await db.Addresses.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt)
            .Select(a => new AddressDto(a.Id, a.FullName, a.Phone, a.DivisionId, a.Division.Name, a.DistrictId, a.District.Name,
                a.AreaId, a.Area.Name, a.AddressLine, a.IsDefault))
            .ToListAsync(ct);

    public async Task<Guid> AddAsync(Guid userId, SaveAddressRequest request, CancellationToken ct = default)
    {
        await CheckLocationsAsync(request, ct);
        var address = new Address { Id = Guid.CreateVersion7(), UserId = userId };
        Fill(address, request);
        var isFirst = !await db.Addresses.AnyAsync(a => a.UserId == userId, ct);
        db.Addresses.Add(address);
        await SaveWithDefaultAsync(userId, address, request.IsDefault || isFirst, ct);
        return address.Id;
    }

    public async Task UpdateAsync(Guid userId, Guid addressId, SaveAddressRequest request, CancellationToken ct = default)
    {
        await CheckLocationsAsync(request, ct);
        var address = await FindOwnAsync(userId, addressId, ct);
        Fill(address, request);
        await SaveWithDefaultAsync(userId, address, request.IsDefault || address.IsDefault, ct);
    }

    public async Task SetDefaultAsync(Guid userId, Guid addressId, CancellationToken ct = default) =>
        await SaveWithDefaultAsync(userId, await FindOwnAsync(userId, addressId, ct), true, ct);

    // Orders keep their own copy of the address (DB-5), so deleting one never changes an old order.
    public async Task DeleteAsync(Guid userId, Guid addressId, CancellationToken ct = default)
    {
        var address = await FindOwnAsync(userId, addressId, ct);
        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);
        if (address.IsDefault && await db.Addresses.FirstOrDefaultAsync(a => a.UserId == userId, ct) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SaveWithDefaultAsync(Guid userId, Address address, bool makeDefault, CancellationToken ct)
    {
        if (!makeDefault)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // The unique filtered index allows one default per user, so clear the old one first.
        var others = await db.Addresses.Where(a => a.UserId == userId && a.IsDefault && a.Id != address.Id).ToListAsync(ct);
        others.ForEach(a => a.IsDefault = false);
        address.IsDefault = false;
        await db.SaveChangesAsync(ct);
        address.IsDefault = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Address> FindOwnAsync(Guid userId, Guid addressId, CancellationToken ct) =>
        await db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, ct)
        ?? throw new AppException(404, Messages.NotFound);

    // SRS 4.7: the district must belong to the division and the area to the district.
    private async Task CheckLocationsAsync(SaveAddressRequest request, CancellationToken ct)
    {
        var ids = new[] { request.DivisionId, request.DistrictId, request.AreaId };
        var found = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var ok = found.TryGetValue(request.DivisionId!.Value, out var division) && division.Level == "Division"
            && found.TryGetValue(request.DistrictId!.Value, out var district) && district.Level == "District" && district.ParentId == division.Id
            && found.TryGetValue(request.AreaId!.Value, out var area) && area.Level == "Area" && area.ParentId == district.Id;
        if (!ok)
        {
            throw new AppException(400, Messages.Invalid);
        }
    }

    private static void Fill(Address address, SaveAddressRequest request)
    {
        address.FullName = request.FullName.Trim();
        address.Phone = request.Phone.Trim();
        address.DivisionId = request.DivisionId!.Value;
        address.DistrictId = request.DistrictId!.Value;
        address.AreaId = request.AreaId!.Value;
        address.AddressLine = request.AddressLine.Trim();
    }
}
