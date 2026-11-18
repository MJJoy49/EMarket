using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public bool EmailConfirmed { get; set; }

    public string Phone { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string SecurityStamp { get; set; } = null!;

    public string Status { get; set; } = null!;

    public bool MustChangePassword { get; set; }

    public string? Gender { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? PhotoUrl { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();

    public virtual ICollection<AspNetUserClaim> AspNetUserClaims { get; set; } = new List<AspNetUserClaim>();

    public virtual ICollection<AspNetUserLogin> AspNetUserLogins { get; set; } = new List<AspNetUserLogin>();

    public virtual ICollection<AspNetUserToken> AspNetUserTokens { get; set; } = new List<AspNetUserToken>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual Cart? Cart { get; set; }

    public virtual ICollection<CheckoutGroup> CheckoutGroups { get; set; } = new List<CheckoutGroup>();

    public virtual ICollection<ContactMessage> ContactMessages { get; set; } = new List<ContactMessage>();

    public virtual ICollection<CouponRedemption> CouponRedemptions { get; set; } = new List<CouponRedemption>();

    public virtual ICollection<EmailToken> EmailTokens { get; set; } = new List<EmailToken>();

    public virtual ICollection<IdempotencyKey> IdempotencyKeys { get; set; } = new List<IdempotencyKey>();

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<Payout> Payouts { get; set; } = new List<Payout>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<ReturnRequest> ReturnRequestBuyers { get; set; } = new List<ReturnRequest>();

    public virtual ICollection<ReturnRequest> ReturnRequestDecidedBies { get; set; } = new List<ReturnRequest>();

    public virtual ICollection<ReviewReport> ReviewReportDecidedBies { get; set; } = new List<ReviewReport>();

    public virtual ICollection<ReviewReport> ReviewReportReporters { get; set; } = new List<ReviewReport>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual ICollection<SellerApplication> SellerApplicationReviewedBies { get; set; } = new List<SellerApplication>();

    public virtual ICollection<SellerApplication> SellerApplicationUsers { get; set; } = new List<SellerApplication>();

    public virtual Shop? Shop { get; set; }

    public virtual ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();

    public virtual ICollection<AspNetRole> Roles { get; set; } = new List<AspNetRole>();
}
