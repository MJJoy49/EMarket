using System;
using System.Collections.Generic;
using EMarket.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EMarket.DAL.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Address> Addresses { get; set; }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Banner> Banners { get; set; }

    public virtual DbSet<Cart> Carts { get; set; }

    public virtual DbSet<CartItem> CartItems { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CheckoutGroup> CheckoutGroups { get; set; }

    public virtual DbSet<ContactMessage> ContactMessages { get; set; }

    public virtual DbSet<Coupon> Coupons { get; set; }

    public virtual DbSet<CouponRedemption> CouponRedemptions { get; set; }

    public virtual DbSet<EmailToken> EmailTokens { get; set; }

    public virtual DbSet<IdempotencyKey> IdempotencyKeys { get; set; }

    public virtual DbSet<LedgerEntry> LedgerEntries { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderItem> OrderItems { get; set; }

    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Payout> Payouts { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductImage> ProductImages { get; set; }

    public virtual DbSet<ProductVariant> ProductVariants { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<Refund> Refunds { get; set; }

    public virtual DbSet<ReturnItem> ReturnItems { get; set; }

    public virtual DbSet<ReturnRequest> ReturnRequests { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<ReviewReport> ReviewReports { get; set; }

    public virtual DbSet<SellerApplication> SellerApplications { get; set; }

    public virtual DbSet<Shop> Shops { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<WishlistItem> WishlistItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("Address");

            entity.HasIndex(e => e.AreaId, "IX_Address_AreaId");

            entity.HasIndex(e => e.DistrictId, "IX_Address_DistrictId");

            entity.HasIndex(e => e.DivisionId, "IX_Address_DivisionId");

            entity.HasIndex(e => e.UserId, "IX_Address_UserId");

            entity.HasIndex(e => e.UserId, "UX_Address_UserId")
                .IsUnique()
                .HasFilter("([IsDefault]=(1))");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Address_Id");
            entity.Property(e => e.AddressLine).HasMaxLength(250);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Address_CreatedAt");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Address_UpdatedAt");

            entity.HasOne(d => d.Area).WithMany(p => p.AddressAreas)
                .HasForeignKey(d => d.AreaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Address_AreaId");

            entity.HasOne(d => d.District).WithMany(p => p.AddressDistricts)
                .HasForeignKey(d => d.DistrictId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Address_DistrictId");

            entity.HasOne(d => d.Division).WithMany(p => p.AddressDivisions)
                .HasForeignKey(d => d.DivisionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Address_DivisionId");

            entity.HasOne(d => d.User).WithMany(p => p.Addresses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Address_UserId");
        });

        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_AspNetRoles_Id");
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.HasIndex(e => e.RoleId, "IX_AspNetRoleClaims_RoleId");

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_AspNetUserClaims_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.ProviderKey).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.Name).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserTokens).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog", tb => tb.HasTrigger("TR_AuditLog_InsertOnly"));

            entity.HasIndex(e => e.CreatedAt, "IX_AuditLog_CreatedAt");

            entity.HasIndex(e => new { e.EntityName, e.EntityId }, "IX_AuditLog_EntityName_EntityId");

            entity.HasIndex(e => e.UserId, "IX_AuditLog_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_AuditLog_Id");
            entity.Property(e => e.Action).HasMaxLength(60);
            entity.Property(e => e.CorrelationId).HasMaxLength(40);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AuditLog_CreatedAt");
            entity.Property(e => e.EntityName).HasMaxLength(60);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.NewValue).HasMaxLength(2000);
            entity.Property(e => e.OldValue).HasMaxLength(2000);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_AuditLog_UpdatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AuditLog_UserId");
        });

        modelBuilder.Entity<Banner>(entity =>
        {
            entity.ToTable("Banner");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Banner_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Banner_CreatedAt");
            entity.Property(e => e.ImageUrl).HasMaxLength(300);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Banner_IsActive");
            entity.Property(e => e.LinkUrl).HasMaxLength(300);
            entity.Property(e => e.Title).HasMaxLength(100);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Banner_UpdatedAt");
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Cart");

            entity.HasIndex(e => e.CouponId, "IX_Cart_CouponId");

            entity.HasIndex(e => e.UserId, "UX_Cart_UserId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Cart_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Cart_CreatedAt");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Cart_UpdatedAt");

            entity.HasOne(d => d.Coupon).WithMany(p => p.Carts)
                .HasForeignKey(d => d.CouponId)
                .HasConstraintName("FK_Cart_CouponId");

            entity.HasOne(d => d.User).WithOne(p => p.Cart)
                .HasForeignKey<Cart>(d => d.UserId)
                .HasConstraintName("FK_Cart_UserId");
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItem");

            entity.HasIndex(e => e.ProductId, "IX_CartItem_ProductId");

            entity.HasIndex(e => e.VariantId, "IX_CartItem_VariantId");

            entity.HasIndex(e => new { e.CartId, e.ProductId, e.VariantId }, "UX_CartItem_CartId_ProductId_VariantId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_CartItem_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CartItem_CreatedAt");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CartItem_UpdatedAt");

            entity.HasOne(d => d.Cart).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.CartId)
                .HasConstraintName("FK_CartItem_CartId");

            entity.HasOne(d => d.Product).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CartItem_ProductId");

            entity.HasOne(d => d.Variant).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.VariantId)
                .HasConstraintName("FK_CartItem_VariantId");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Category");

            entity.HasIndex(e => new { e.ParentId, e.Name }, "UX_Category_ParentId_Name").IsUnique();

            entity.HasIndex(e => e.Slug, "UX_Category_Slug").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Category_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Category_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Category_IsActive");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Slug).HasMaxLength(120);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Category_UpdatedAt");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_Category_ParentId");
        });

        modelBuilder.Entity<CheckoutGroup>(entity =>
        {
            entity.ToTable("CheckoutGroup");

            entity.HasIndex(e => e.BuyerId, "IX_CheckoutGroup_BuyerId");

            entity.HasIndex(e => e.IdempotencyKey, "UX_CheckoutGroup_IdempotencyKey").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_CheckoutGroup_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CheckoutGroup_CreatedAt");
            entity.Property(e => e.CreditUsed).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DeliveryTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(64);
            entity.Property(e => e.ItemsTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PayableTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CheckoutGroup_UpdatedAt");

            entity.HasOne(d => d.Buyer).WithMany(p => p.CheckoutGroups)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CheckoutGroup_BuyerId");
        });

        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("ContactMessage");

            entity.HasIndex(e => e.HandledById, "IX_ContactMessage_HandledById");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ContactMessage_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ContactMessage_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.Message).HasMaxLength(2000);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("New", "DF_ContactMessage_Status");
            entity.Property(e => e.Subject).HasMaxLength(150);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ContactMessage_UpdatedAt");

            entity.HasOne(d => d.HandledBy).WithMany(p => p.ContactMessages)
                .HasForeignKey(d => d.HandledById)
                .HasConstraintName("FK_ContactMessage_HandledById");
        });

        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable("Coupon");

            entity.HasIndex(e => e.Code, "UX_Coupon_Code").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Coupon_Id");
            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Coupon_CreatedAt");
            entity.Property(e => e.DiscountType).HasMaxLength(20);
            entity.Property(e => e.DiscountValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Coupon_IsActive");
            entity.Property(e => e.MaxDiscount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.MinOrderAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PerUserLimit).HasDefaultValue(1, "DF_Coupon_PerUserLimit");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Coupon_UpdatedAt");
        });

        modelBuilder.Entity<CouponRedemption>(entity =>
        {
            entity.ToTable("CouponRedemption");

            entity.HasIndex(e => e.CouponId, "IX_CouponRedemption_CouponId");

            entity.HasIndex(e => e.UserId, "IX_CouponRedemption_UserId");

            entity.HasIndex(e => e.CheckoutGroupId, "UX_CouponRedemption_CheckoutGroupId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_CouponRedemption_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CouponRedemption_CreatedAt");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_CouponRedemption_UpdatedAt");

            entity.HasOne(d => d.CheckoutGroup).WithOne(p => p.CouponRedemption)
                .HasForeignKey<CouponRedemption>(d => d.CheckoutGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CouponRedemption_CheckoutGroupId");

            entity.HasOne(d => d.Coupon).WithMany(p => p.CouponRedemptions)
                .HasForeignKey(d => d.CouponId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CouponRedemption_CouponId");

            entity.HasOne(d => d.User).WithMany(p => p.CouponRedemptions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CouponRedemption_UserId");
        });

        modelBuilder.Entity<EmailToken>(entity =>
        {
            entity.ToTable("EmailToken");

            entity.HasIndex(e => e.UserId, "IX_EmailToken_UserId");

            entity.HasIndex(e => e.TokenHash, "UX_EmailToken_TokenHash").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_EmailToken_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_EmailToken_CreatedAt");
            entity.Property(e => e.Purpose).HasMaxLength(20);
            entity.Property(e => e.TokenHash).HasMaxLength(88);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_EmailToken_UpdatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.EmailTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_EmailToken_UserId");
        });

        modelBuilder.Entity<IdempotencyKey>(entity =>
        {
            entity.ToTable("IdempotencyKey");

            entity.HasIndex(e => e.ExpiresAt, "IX_IdempotencyKey_ExpiresAt");

            entity.HasIndex(e => e.UserId, "IX_IdempotencyKey_UserId");

            entity.HasIndex(e => new { e.Key, e.UserId }, "UX_IdempotencyKey_Key_UserId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_IdempotencyKey_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_IdempotencyKey_CreatedAt");
            entity.Property(e => e.Endpoint).HasMaxLength(100);
            entity.Property(e => e.Key).HasMaxLength(64);
            entity.Property(e => e.RequestHash).HasMaxLength(64);
            entity.Property(e => e.ResponseBody).HasMaxLength(4000);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_IdempotencyKey_UpdatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.IdempotencyKeys)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_IdempotencyKey_UserId");
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("LedgerEntry", tb => tb.HasTrigger("TR_LedgerEntry_InsertOnly"));

            entity.HasIndex(e => e.OrderId, "IX_LedgerEntry_OrderId");

            entity.HasIndex(e => new { e.ShopId, e.Account }, "IX_LedgerEntry_ShopId_Account");

            entity.HasIndex(e => new { e.UserId, e.Account }, "IX_LedgerEntry_UserId_Account");

            entity.HasIndex(e => new { e.TransactionId, e.Account, e.RefId }, "UX_LedgerEntry_TransactionId_Account_RefId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_LedgerEntry_Id");
            entity.Property(e => e.Account).HasMaxLength(30);
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_LedgerEntry_CreatedAt");
            entity.Property(e => e.Kind).HasMaxLength(30);
            entity.Property(e => e.Note).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_LedgerEntry_UpdatedAt");

            entity.HasOne(d => d.Order).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_LedgerEntry_OrderId");

            entity.HasOne(d => d.Shop).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("FK_LedgerEntry_ShopId");

            entity.HasOne(d => d.User).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_LedgerEntry_UserId");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Location");

            entity.HasIndex(e => new { e.ParentId, e.Level }, "IX_Location_ParentId_Level");

            entity.HasIndex(e => new { e.ParentId, e.Name }, "UX_Location_ParentId_Name").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Location_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Location_CreatedAt");
            entity.Property(e => e.Level).HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Location_UpdatedAt");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("FK_Location_ParentId");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notification");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_Notification_UserId_IsRead_CreatedAt");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Notification_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Notification_CreatedAt");
            entity.Property(e => e.LinkUrl).HasMaxLength(300);
            entity.Property(e => e.Message).HasMaxLength(400);
            entity.Property(e => e.Title).HasMaxLength(100);
            entity.Property(e => e.Type).HasMaxLength(40);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Notification_UpdatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Notification_UserId");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Order");

            entity.HasIndex(e => e.BuyerId, "IX_Order_BuyerId");

            entity.HasIndex(e => e.CheckoutGroupId, "IX_Order_CheckoutGroupId");

            entity.HasIndex(e => e.CouponId, "IX_Order_CouponId");

            entity.HasIndex(e => e.CreatedAt, "IX_Order_CreatedAt");

            entity.HasIndex(e => e.ShipAreaId, "IX_Order_ShipAreaId");

            entity.HasIndex(e => e.ShopId, "IX_Order_ShopId");

            entity.HasIndex(e => e.Status, "IX_Order_Status");

            entity.HasIndex(e => e.OrderNumber, "UX_Order_OrderNumber").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Order_Id");
            entity.Property(e => e.CommissionAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CommissionPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.CourierName).HasMaxLength(60);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Order_CreatedAt");
            entity.Property(e => e.CreditUsed).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DeliveryFee).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ItemsTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OrderNumber)
                .HasMaxLength(20)
                .HasDefaultValueSql("((('EM-'+CONVERT([char](8),sysutcdatetime(),(112)))+'-')+right('000000'+CONVERT([varchar](10),NEXT VALUE FOR [dbo].[OrderNumberSeq]),(6)))", "DF_Order_OrderNumber");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.ShipAddress).HasMaxLength(400);
            entity.Property(e => e.ShipName).HasMaxLength(100);
            entity.Property(e => e.ShipPhone).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TrackingNote).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Order_UpdatedAt");

            entity.HasOne(d => d.Buyer).WithMany(p => p.Orders)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Order_BuyerId");

            entity.HasOne(d => d.CheckoutGroup).WithMany(p => p.Orders)
                .HasForeignKey(d => d.CheckoutGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Order_CheckoutGroupId");

            entity.HasOne(d => d.Coupon).WithMany(p => p.Orders)
                .HasForeignKey(d => d.CouponId)
                .HasConstraintName("FK_Order_CouponId");

            entity.HasOne(d => d.ShipArea).WithMany(p => p.Orders)
                .HasForeignKey(d => d.ShipAreaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Order_ShipAreaId");

            entity.HasOne(d => d.Shop).WithMany(p => p.Orders)
                .HasForeignKey(d => d.ShopId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Order_ShopId");
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItem");

            entity.HasIndex(e => e.OrderId, "IX_OrderItem_OrderId");

            entity.HasIndex(e => e.ProductId, "IX_OrderItem_ProductId");

            entity.HasIndex(e => e.VariantId, "IX_OrderItem_VariantId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_OrderItem_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OrderItem_CreatedAt");
            entity.Property(e => e.ProductName).HasMaxLength(200);
            entity.Property(e => e.Sku).HasMaxLength(50);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OrderItem_UpdatedAt");
            entity.Property(e => e.VariantText).HasMaxLength(100);

            entity.HasOne(d => d.Order).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_OrderItem_OrderId");

            entity.HasOne(d => d.Product).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderItem_ProductId");

            entity.HasOne(d => d.Variant).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.VariantId)
                .HasConstraintName("FK_OrderItem_VariantId");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistory", tb => tb.HasTrigger("TR_OrderStatusHistory_InsertOnly"));

            entity.HasIndex(e => e.ChangedById, "IX_OrderStatusHistory_ChangedById");

            entity.HasIndex(e => e.OrderId, "IX_OrderStatusHistory_OrderId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_OrderStatusHistory_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OrderStatusHistory_CreatedAt");
            entity.Property(e => e.FromStatus).HasMaxLength(20);
            entity.Property(e => e.Note).HasMaxLength(300);
            entity.Property(e => e.ToStatus).HasMaxLength(20);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OrderStatusHistory_UpdatedAt");

            entity.HasOne(d => d.ChangedBy).WithMany(p => p.OrderStatusHistories)
                .HasForeignKey(d => d.ChangedById)
                .HasConstraintName("FK_OrderStatusHistory_ChangedById");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderStatusHistories)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderStatusHistory_OrderId");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessage");

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt }, "IX_OutboxMessage_Status_NextAttemptAt");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_OutboxMessage_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OutboxMessage_CreatedAt");
            entity.Property(e => e.LastError).HasMaxLength(500);
            entity.Property(e => e.NextAttemptAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OutboxMessage_NextAttemptAt");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending", "DF_OutboxMessage_Status");
            entity.Property(e => e.Type).HasMaxLength(100);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_OutboxMessage_UpdatedAt");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payment");

            entity.HasIndex(e => e.BuyerId, "IX_Payment_BuyerId");

            entity.HasIndex(e => e.CheckoutGroupId, "IX_Payment_CheckoutGroupId");

            entity.HasIndex(e => e.LastCheckedAt, "IX_Payment_LastCheckedAt");

            entity.HasIndex(e => e.Status, "IX_Payment_Status");

            entity.HasIndex(e => e.TransactionId, "UX_Payment_TransactionId")
                .IsUnique()
                .HasFilter("([TransactionId] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Payment_Id");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Payment_CreatedAt");
            entity.Property(e => e.GatewayStatus).HasMaxLength(30);
            entity.Property(e => e.Method).HasMaxLength(20);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.TransactionId).HasMaxLength(100);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Payment_UpdatedAt");

            entity.HasOne(d => d.Buyer).WithMany(p => p.Payments)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payment_BuyerId");

            entity.HasOne(d => d.CheckoutGroup).WithMany(p => p.Payments)
                .HasForeignKey(d => d.CheckoutGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payment_CheckoutGroupId");
        });

        modelBuilder.Entity<Payout>(entity =>
        {
            entity.ToTable("Payout");

            entity.HasIndex(e => e.DecidedById, "IX_Payout_DecidedById");

            entity.HasIndex(e => e.ShopId, "IX_Payout_ShopId");

            entity.HasIndex(e => e.Status, "IX_Payout_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Payout_Id");
            entity.Property(e => e.AccountSnapshot).HasMaxLength(200);
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Payout_CreatedAt");
            entity.Property(e => e.Method).HasMaxLength(20);
            entity.Property(e => e.ReferenceNo).HasMaxLength(100);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Requested", "DF_Payout_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Payout_UpdatedAt");

            entity.HasOne(d => d.DecidedBy).WithMany(p => p.Payouts)
                .HasForeignKey(d => d.DecidedById)
                .HasConstraintName("FK_Payout_DecidedById");

            entity.HasOne(d => d.Shop).WithMany(p => p.Payouts)
                .HasForeignKey(d => d.ShopId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payout_ShopId");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Product");

            entity.HasIndex(e => e.CategoryId, "IX_Product_CategoryId");

            entity.HasIndex(e => e.Name, "IX_Product_Name");

            entity.HasIndex(e => e.Price, "IX_Product_Price");

            entity.HasIndex(e => e.ShopId, "IX_Product_ShopId");

            entity.HasIndex(e => e.Status, "IX_Product_Status");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Product_Id");
            entity.Property(e => e.AverageRating).HasColumnType("decimal(3, 2)");
            entity.Property(e => e.Brand).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Product_CreatedAt");
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.DiscountPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Draft", "DF_Product_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Product_UpdatedAt");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Product_CategoryId");

            entity.HasOne(d => d.Shop).WithMany(p => p.Products)
                .HasForeignKey(d => d.ShopId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Product_ShopId");
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImage");

            entity.HasIndex(e => new { e.ProductId, e.SortOrder }, "UX_ProductImage_ProductId_SortOrder").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ProductImage_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ProductImage_CreatedAt");
            entity.Property(e => e.Path).HasMaxLength(300);
            entity.Property(e => e.SortOrder).HasDefaultValue(1, "DF_ProductImage_SortOrder");
            entity.Property(e => e.ThumbPath).HasMaxLength(300);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ProductImage_UpdatedAt");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductImages)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_ProductImage_ProductId");
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariant");

            entity.HasIndex(e => e.Sku, "IX_ProductVariant_Sku");

            entity.HasIndex(e => new { e.ProductId, e.Sku }, "UX_ProductVariant_ProductId_Sku").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ProductVariant_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ProductVariant_CreatedAt");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_ProductVariant_IsActive");
            entity.Property(e => e.OptionText).HasMaxLength(100);
            entity.Property(e => e.PriceOverride).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Sku).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ProductVariant_UpdatedAt");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductVariants)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProductVariant_ProductId");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshToken");

            entity.HasIndex(e => e.ExpiresAt, "IX_RefreshToken_ExpiresAt");

            entity.HasIndex(e => e.ReplacedByTokenId, "IX_RefreshToken_ReplacedByTokenId");

            entity.HasIndex(e => e.UserId, "IX_RefreshToken_UserId");

            entity.HasIndex(e => e.TokenHash, "UX_RefreshToken_TokenHash").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_RefreshToken_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_RefreshToken_CreatedAt");
            entity.Property(e => e.CreatedByIp).HasMaxLength(45);
            entity.Property(e => e.TokenHash).HasMaxLength(88);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_RefreshToken_UpdatedAt");

            entity.HasOne(d => d.ReplacedByToken).WithMany(p => p.InverseReplacedByToken)
                .HasForeignKey(d => d.ReplacedByTokenId)
                .HasConstraintName("FK_RefreshToken_ReplacedByTokenId");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_RefreshToken_UserId");
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.ToTable("Refund");

            entity.HasIndex(e => e.OrderId, "IX_Refund_OrderId");

            entity.HasIndex(e => e.PaymentId, "IX_Refund_PaymentId");

            entity.HasIndex(e => e.ReturnRequestId, "IX_Refund_ReturnRequestId");

            entity.HasIndex(e => e.GatewayRefundId, "UX_Refund_GatewayRefundId")
                .IsUnique()
                .HasFilter("([GatewayRefundId] IS NOT NULL)");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Refund_Id");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Refund_CreatedAt");
            entity.Property(e => e.GatewayRefundId).HasMaxLength(100);
            entity.Property(e => e.Method).HasMaxLength(20);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending", "DF_Refund_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Refund_UpdatedAt");

            entity.HasOne(d => d.Order).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Refund_OrderId");

            entity.HasOne(d => d.Payment).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Refund_PaymentId");

            entity.HasOne(d => d.ReturnRequest).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.ReturnRequestId)
                .HasConstraintName("FK_Refund_ReturnRequestId");
        });

        modelBuilder.Entity<ReturnItem>(entity =>
        {
            entity.ToTable("ReturnItem");

            entity.HasIndex(e => e.OrderItemId, "IX_ReturnItem_OrderItemId");

            entity.HasIndex(e => e.ReturnRequestId, "IX_ReturnItem_ReturnRequestId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ReturnItem_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReturnItem_CreatedAt");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReturnItem_UpdatedAt");

            entity.HasOne(d => d.OrderItem).WithMany(p => p.ReturnItems)
                .HasForeignKey(d => d.OrderItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReturnItem_OrderItemId");

            entity.HasOne(d => d.ReturnRequest).WithMany(p => p.ReturnItems)
                .HasForeignKey(d => d.ReturnRequestId)
                .HasConstraintName("FK_ReturnItem_ReturnRequestId");
        });

        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            entity.ToTable("ReturnRequest");

            entity.HasIndex(e => e.BuyerId, "IX_ReturnRequest_BuyerId");

            entity.HasIndex(e => e.DecidedById, "IX_ReturnRequest_DecidedById");

            entity.HasIndex(e => e.OrderId, "IX_ReturnRequest_OrderId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ReturnRequest_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReturnRequest_CreatedAt");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.PhotoPaths).HasMaxLength(1500);
            entity.Property(e => e.Reason).HasMaxLength(30);
            entity.Property(e => e.RefundMethod).HasMaxLength(20);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Requested", "DF_ReturnRequest_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReturnRequest_UpdatedAt");

            entity.HasOne(d => d.Buyer).WithMany(p => p.ReturnRequestBuyers)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReturnRequest_BuyerId");

            entity.HasOne(d => d.DecidedBy).WithMany(p => p.ReturnRequestDecidedBies)
                .HasForeignKey(d => d.DecidedById)
                .HasConstraintName("FK_ReturnRequest_DecidedById");

            entity.HasOne(d => d.Order).WithMany(p => p.ReturnRequests)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReturnRequest_OrderId");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Review");

            entity.HasIndex(e => e.BuyerId, "IX_Review_BuyerId");

            entity.HasIndex(e => new { e.ProductId, e.Status }, "IX_Review_ProductId_Status");

            entity.HasIndex(e => e.OrderItemId, "UX_Review_OrderItemId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Review_Id");
            entity.Property(e => e.Comment).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Review_CreatedAt");
            entity.Property(e => e.SellerReply).HasMaxLength(500);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Visible", "DF_Review_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Review_UpdatedAt");

            entity.HasOne(d => d.Buyer).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Review_BuyerId");

            entity.HasOne(d => d.OrderItem).WithOne(p => p.Review)
                .HasForeignKey<Review>(d => d.OrderItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Review_OrderItemId");

            entity.HasOne(d => d.Product).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Review_ProductId");
        });

        modelBuilder.Entity<ReviewReport>(entity =>
        {
            entity.ToTable("ReviewReport");

            entity.HasIndex(e => e.DecidedById, "IX_ReviewReport_DecidedById");

            entity.HasIndex(e => e.ReporterId, "IX_ReviewReport_ReporterId");

            entity.HasIndex(e => new { e.ReviewId, e.ReporterId }, "UX_ReviewReport_ReviewId_ReporterId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_ReviewReport_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReviewReport_CreatedAt");
            entity.Property(e => e.Reason).HasMaxLength(300);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Open", "DF_ReviewReport_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_ReviewReport_UpdatedAt");

            entity.HasOne(d => d.DecidedBy).WithMany(p => p.ReviewReportDecidedBies)
                .HasForeignKey(d => d.DecidedById)
                .HasConstraintName("FK_ReviewReport_DecidedById");

            entity.HasOne(d => d.Reporter).WithMany(p => p.ReviewReportReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReviewReport_ReporterId");

            entity.HasOne(d => d.Review).WithMany(p => p.ReviewReports)
                .HasForeignKey(d => d.ReviewId)
                .HasConstraintName("FK_ReviewReport_ReviewId");
        });

        modelBuilder.Entity<SellerApplication>(entity =>
        {
            entity.ToTable("SellerApplication");

            entity.HasIndex(e => e.ReviewedById, "IX_SellerApplication_ReviewedById");

            entity.HasIndex(e => e.Status, "IX_SellerApplication_Status");

            entity.HasIndex(e => e.UserId, "IX_SellerApplication_UserId");

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_SellerApplication_Id");
            entity.Property(e => e.BusinessType)
                .HasMaxLength(20)
                .HasDefaultValue("Individual", "DF_SellerApplication_BusinessType");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_SellerApplication_CreatedAt");
            entity.Property(e => e.NidBackPath).HasMaxLength(300);
            entity.Property(e => e.NidFrontPath).HasMaxLength(300);
            entity.Property(e => e.NidNumber).HasMaxLength(200);
            entity.Property(e => e.PayoutAccount).HasMaxLength(200);
            entity.Property(e => e.PayoutMethod).HasMaxLength(20);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.ShopDescription).HasMaxLength(1000);
            entity.Property(e => e.ShopName).HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending", "DF_SellerApplication_Status");
            entity.Property(e => e.TradeLicencePath).HasMaxLength(300);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_SellerApplication_UpdatedAt");

            entity.HasOne(d => d.ReviewedBy).WithMany(p => p.SellerApplicationReviewedBies)
                .HasForeignKey(d => d.ReviewedById)
                .HasConstraintName("FK_SellerApplication_ReviewedById");

            entity.HasOne(d => d.User).WithMany(p => p.SellerApplicationUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SellerApplication_UserId");
        });

        modelBuilder.Entity<Shop>(entity =>
        {
            entity.ToTable("Shop");

            entity.HasIndex(e => e.Status, "IX_Shop_Status");

            entity.HasIndex(e => e.Name, "UX_Shop_Name").IsUnique();

            entity.HasIndex(e => e.OwnerId, "UX_Shop_OwnerId").IsUnique();

            entity.HasIndex(e => e.Slug, "UX_Shop_Slug").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_Shop_Id");
            entity.Property(e => e.BannerUrl).HasMaxLength(300);
            entity.Property(e => e.CommissionPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Shop_CreatedAt");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.FreeDeliveryAbove)
                .HasDefaultValue(2000m, "DF_Shop_FreeDeliveryAbove")
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.InsideDhakaFee)
                .HasDefaultValue(60m, "DF_Shop_InsideDhakaFee")
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.LogoUrl).HasMaxLength(300);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.OutsideDhakaFee)
                .HasDefaultValue(60m, "DF_Shop_OutsideDhakaFee")
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PayoutAccount).HasMaxLength(200);
            entity.Property(e => e.PayoutMethod).HasMaxLength(20);
            entity.Property(e => e.Slug).HasMaxLength(120);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active", "DF_Shop_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_Shop_UpdatedAt");

            entity.HasOne(d => d.Owner).WithOne(p => p.Shop)
                .HasForeignKey<Shop>(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Shop_OwnerId");
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("SystemSetting");

            entity.HasIndex(e => e.Key, "UX_SystemSetting_Key").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_SystemSetting_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_SystemSetting_CreatedAt");
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.Key).HasMaxLength(60);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_SystemSetting_UpdatedAt");
            entity.Property(e => e.Value).HasMaxLength(4000);
            entity.Property(e => e.ValueType).HasMaxLength(20);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");

            entity.HasIndex(e => e.Status, "IX_User_Status");

            entity.HasIndex(e => e.Email, "UX_User_Email").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_User_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_User_CreatedAt");
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Gender).HasMaxLength(20);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.PhotoUrl).HasMaxLength(300);
            entity.Property(e => e.SecurityStamp).HasMaxLength(64);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active", "DF_User_Status");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_User_UpdatedAt");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<User>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<WishlistItem>(entity =>
        {
            entity.ToTable("WishlistItem");

            entity.HasIndex(e => e.ProductId, "IX_WishlistItem_ProductId");

            entity.HasIndex(e => new { e.UserId, e.ProductId }, "UX_WishlistItem_UserId_ProductId").IsUnique();

            entity.Property(e => e.Id).HasDefaultValueSql("(newsequentialid())", "DF_WishlistItem_Id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_WishlistItem_CreatedAt");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())", "DF_WishlistItem_UpdatedAt");

            entity.HasOne(d => d.Product).WithMany(p => p.WishlistItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_WishlistItem_ProductId");

            entity.HasOne(d => d.User).WithMany(p => p.WishlistItems)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_WishlistItem_UserId");
        });
        modelBuilder.HasSequence<int>("OrderNumberSeq")
            .HasMin(1L)
            .HasMax(999999L);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
