/* =====================================================================
   EMarketDb - full database script (SQL Server 2019+ / Express)
   Source : EMarket SRS v2.0, Section 6 (data model, 35 tables)
   Author : M.J. Joy

   HOW TO RUN
     1. SSMS -> File -> Open -> File... -> pick this file.
     2. Press F5 (Execute). It takes a few seconds.
     3. Refresh "Databases" - you will see EMarketDb with all tables.
   TO START OVER
     ALTER DATABASE EMarketDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
     USE master; DROP DATABASE EMarketDb;      -- then run this file again
   The script refuses to run when EMarketDb already exists (nothing is changed).

   RULES COVERED : DB-1 to DB-15 (see notes at the end of the file).
   NOT in this file : the first Admin user (the app creates it at first start,
                      BR-46) and demo data.
   ===================================================================== */

USE master;
GO
IF DB_ID(N'EMarketDb') IS NOT NULL
BEGIN
    PRINT N'EMarketDb already exists - nothing was changed. Drop it first (see the top of this file).';
    SET NOEXEC ON;
END
GO
CREATE DATABASE EMarketDb;
GO
USE EMarketDb;
GO
SET XACT_ABORT ON;
GO

/* ---------- Order number sequence (DB-6): the 6 digits come from here ---------- */
CREATE SEQUENCE dbo.OrderNumberSeq AS INT START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 999999 NO CYCLE;
GO

/* ---------- Tables (35) ---------- */

-- Platform services
CREATE TABLE dbo.[Location] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Location_Id] DEFAULT NEWSEQUENTIALID(),
    [ParentId] UNIQUEIDENTIFIER NULL,
    [Level] NVARCHAR(20) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [IsInsideDhaka] BIT NOT NULL CONSTRAINT [DF_Location_IsInsideDhaka] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Location_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Location_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Location] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Location_Level] CHECK ([Level] IN ('Division', 'District', 'Area')),
    CONSTRAINT [CK_Location_ParentByLevel] CHECK (([Level] = 'Division' AND [ParentId] IS NULL) OR ([Level] <> 'Division' AND [ParentId] IS NOT NULL))
);
GO

-- Identity
CREATE TABLE dbo.[User] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_User_Id] DEFAULT NEWSEQUENTIALID(),
    [FullName] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(150) NOT NULL,
    [EmailConfirmed] BIT NOT NULL CONSTRAINT [DF_User_EmailConfirmed] DEFAULT 0,
    [Phone] NVARCHAR(20) NOT NULL,
    [PasswordHash] NVARCHAR(MAX) NOT NULL,
    [SecurityStamp] NVARCHAR(64) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_User_Status] DEFAULT 'Active',
    [MustChangePassword] BIT NOT NULL CONSTRAINT [DF_User_MustChangePassword] DEFAULT 0,
    [Gender] NVARCHAR(20) NULL,
    [BirthDate] DATE NULL,
    [PhotoUrl] NVARCHAR(300) NULL,
    [FailedLoginCount] INT NOT NULL CONSTRAINT [DF_User_FailedLoginCount] DEFAULT 0,
    [LockoutEnd] DATETIME2(7) NULL,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_User_IsDeleted] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_User_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_User_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_User_Status] CHECK ([Status] IN ('Active', 'Blocked', 'Deleted')),
    CONSTRAINT [CK_User_Gender] CHECK ([Gender] IN ('Male', 'Female')),
    CONSTRAINT [CK_User_FailedLoginCount] CHECK ([FailedLoginCount] >= 0)
);
GO

-- Identity
CREATE TABLE dbo.[RefreshToken] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_RefreshToken_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(88) NOT NULL,
    [FamilyId] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [UsedAt] DATETIME2(7) NULL,
    [RevokedAt] DATETIME2(7) NULL,
    [ReplacedByTokenId] UNIQUEIDENTIFIER NULL,
    [CreatedByIp] NVARCHAR(45) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_RefreshToken_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_RefreshToken_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_RefreshToken] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Identity
CREATE TABLE dbo.[EmailToken] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_EmailToken_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Purpose] NVARCHAR(20) NOT NULL,
    [TokenHash] NVARCHAR(88) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [UsedAt] DATETIME2(7) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_EmailToken_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_EmailToken_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EmailToken] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_EmailToken_Purpose] CHECK ([Purpose] IN ('VerifyEmail', 'ResetPassword'))
);
GO

-- Identity
CREATE TABLE dbo.[Address] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Address_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [FullName] NVARCHAR(100) NOT NULL,
    [Phone] NVARCHAR(20) NOT NULL,
    [DivisionId] UNIQUEIDENTIFIER NOT NULL,
    [DistrictId] UNIQUEIDENTIFIER NOT NULL,
    [AreaId] UNIQUEIDENTIFIER NOT NULL,
    [AddressLine] NVARCHAR(250) NOT NULL,
    [IsDefault] BIT NOT NULL CONSTRAINT [DF_Address_IsDefault] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Address_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Address_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Address] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Identity
CREATE TABLE dbo.[AuditLog] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AuditLog_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [Action] NVARCHAR(60) NOT NULL,
    [EntityName] NVARCHAR(60) NOT NULL,
    [EntityId] UNIQUEIDENTIFIER NULL,
    [OldValue] NVARCHAR(2000) NULL,
    [NewValue] NVARCHAR(2000) NULL,
    [CorrelationId] NVARCHAR(40) NULL,
    [IpAddress] NVARCHAR(45) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuditLog_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuditLog_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Sellers
CREATE TABLE dbo.[SellerApplication] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_SellerApplication_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ShopName] NVARCHAR(100) NOT NULL,
    [ShopDescription] NVARCHAR(1000) NOT NULL,
    [BusinessType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_SellerApplication_BusinessType] DEFAULT 'Individual',
    [NidNumber] NVARCHAR(200) NOT NULL,
    [NidFrontPath] NVARCHAR(300) NOT NULL,
    [NidBackPath] NVARCHAR(300) NOT NULL,
    [TradeLicencePath] NVARCHAR(300) NULL,
    [PayoutMethod] NVARCHAR(20) NOT NULL,
    [PayoutAccount] NVARCHAR(200) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_SellerApplication_Status] DEFAULT 'Pending',
    [ReviewedById] UNIQUEIDENTIFIER NULL,
    [ReviewedAt] DATETIME2(7) NULL,
    [RejectionReason] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SellerApplication_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SellerApplication_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SellerApplication] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_SellerApplication_BusinessType] CHECK ([BusinessType] IN ('Individual', 'Business')),
    CONSTRAINT [CK_SellerApplication_PayoutMethod] CHECK ([PayoutMethod] IN ('BankTransfer', 'bKash', 'Nagad')),
    CONSTRAINT [CK_SellerApplication_Status] CHECK ([Status] IN ('Pending', 'Approved', 'Rejected')),
    CONSTRAINT [CK_SellerApplication_RejectionNeedsReason] CHECK ([Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL)
);
GO

-- Sellers
CREATE TABLE dbo.[Shop] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Shop_Id] DEFAULT NEWSEQUENTIALID(),
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Slug] NVARCHAR(120) NOT NULL,
    [Description] NVARCHAR(1000) NOT NULL,
    [LogoUrl] NVARCHAR(300) NULL,
    [BannerUrl] NVARCHAR(300) NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Shop_Status] DEFAULT 'Active',
    [CommissionPercent] DECIMAL(5,2) NULL,
    [InsideDhakaFee] DECIMAL(18,2) NULL CONSTRAINT [DF_Shop_InsideDhakaFee] DEFAULT 60,
    [OutsideDhakaFee] DECIMAL(18,2) NULL CONSTRAINT [DF_Shop_OutsideDhakaFee] DEFAULT 60,
    [FreeDeliveryAbove] DECIMAL(18,2) NULL CONSTRAINT [DF_Shop_FreeDeliveryAbove] DEFAULT 2000,
    [PayoutMethod] NVARCHAR(20) NOT NULL,
    [PayoutAccount] NVARCHAR(200) NOT NULL,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Shop_IsDeleted] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Shop_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Shop_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Shop] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Shop_Status] CHECK ([Status] IN ('Active', 'Vacation', 'Suspended')),
    CONSTRAINT [CK_Shop_CommissionPercent] CHECK ([CommissionPercent] BETWEEN 0 AND 100),
    CONSTRAINT [CK_Shop_InsideDhakaFee] CHECK ([InsideDhakaFee] >= 0),
    CONSTRAINT [CK_Shop_OutsideDhakaFee] CHECK ([OutsideDhakaFee] >= 0),
    CONSTRAINT [CK_Shop_FreeDeliveryAbove] CHECK ([FreeDeliveryAbove] >= 0),
    CONSTRAINT [CK_Shop_PayoutMethod] CHECK ([PayoutMethod] IN ('BankTransfer', 'bKash', 'Nagad'))
);
GO

-- Promotions
CREATE TABLE dbo.[Coupon] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Coupon_Id] DEFAULT NEWSEQUENTIALID(),
    [Code] NVARCHAR(20) NOT NULL,
    [DiscountType] NVARCHAR(20) NOT NULL,
    [DiscountValue] DECIMAL(18,2) NOT NULL,
    [MaxDiscount] DECIMAL(18,2) NULL,
    [MinOrderAmount] DECIMAL(18,2) NULL,
    [StartsAt] DATETIME2(7) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [UsageLimit] INT NULL,
    [PerUserLimit] INT NOT NULL CONSTRAINT [DF_Coupon_PerUserLimit] DEFAULT 1,
    [UsedCount] INT NOT NULL CONSTRAINT [DF_Coupon_UsedCount] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Coupon_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Coupon_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Coupon_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Coupon] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Coupon_CodeFormat] CHECK (LEN([Code]) BETWEEN 4 AND 20 AND [Code] NOT LIKE '%[^A-Za-z0-9]%'),
    CONSTRAINT [CK_Coupon_DiscountType] CHECK ([DiscountType] IN ('Percent', 'Fixed')),
    CONSTRAINT [CK_Coupon_DiscountValue] CHECK ([DiscountValue] > 0),
    CONSTRAINT [CK_Coupon_PercentMax100] CHECK ([DiscountType] <> 'Percent' OR [DiscountValue] <= 100),
    CONSTRAINT [CK_Coupon_MaxDiscount] CHECK ([MaxDiscount] > 0),
    CONSTRAINT [CK_Coupon_MinOrderAmount] CHECK ([MinOrderAmount] >= 0),
    CONSTRAINT [CK_Coupon_DatesOrder] CHECK ([ExpiresAt] > [StartsAt]),
    CONSTRAINT [CK_Coupon_UsageLimit] CHECK ([UsageLimit] >= 1),
    CONSTRAINT [CK_Coupon_PerUserLimit] CHECK ([PerUserLimit] >= 1),
    CONSTRAINT [CK_Coupon_UsedCount] CHECK ([UsedCount] >= 0),
    CONSTRAINT [CK_Coupon_UsedWithinLimit] CHECK ([UsageLimit] IS NULL OR [UsedCount] <= [UsageLimit])
);
GO

-- Catalog
CREATE TABLE dbo.[Category] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Category_Id] DEFAULT NEWSEQUENTIALID(),
    [ParentId] UNIQUEIDENTIFIER NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Slug] NVARCHAR(120) NOT NULL,
    [SortOrder] INT NOT NULL CONSTRAINT [DF_Category_SortOrder] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Category_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Category_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Category_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Category] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Catalog
CREATE TABLE dbo.[Product] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Product_Id] DEFAULT NEWSEQUENTIALID(),
    [ShopId] UNIQUEIDENTIFIER NOT NULL,
    [CategoryId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Brand] NVARCHAR(100) NULL,
    [Description] NVARCHAR(4000) NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    [DiscountPercent] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_Product_DiscountPercent] DEFAULT 0,
    [Stock] INT NOT NULL CONSTRAINT [DF_Product_Stock] DEFAULT 0,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Product_Status] DEFAULT 'Draft',
    [RejectionReason] NVARCHAR(500) NULL,
    [AverageRating] DECIMAL(3,2) NOT NULL CONSTRAINT [DF_Product_AverageRating] DEFAULT 0,
    [ReviewCount] INT NOT NULL CONSTRAINT [DF_Product_ReviewCount] DEFAULT 0,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Product_IsDeleted] DEFAULT 0,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Product_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Product_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Product] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Product_Price] CHECK ([Price] > 0),
    CONSTRAINT [CK_Product_DiscountPercent] CHECK ([DiscountPercent] BETWEEN 0 AND 100),
    CONSTRAINT [CK_Product_Stock] CHECK ([Stock] >= 0),
    CONSTRAINT [CK_Product_Status] CHECK ([Status] IN ('Draft', 'Pending', 'Active', 'Rejected', 'Hidden')),
    CONSTRAINT [CK_Product_AverageRating] CHECK ([AverageRating] BETWEEN 0 AND 5),
    CONSTRAINT [CK_Product_ReviewCount] CHECK ([ReviewCount] >= 0)
);
GO

-- Catalog
CREATE TABLE dbo.[ProductImage] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ProductImage_Id] DEFAULT NEWSEQUENTIALID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Path] NVARCHAR(300) NOT NULL,
    [ThumbPath] NVARCHAR(300) NOT NULL,
    [SortOrder] INT NOT NULL CONSTRAINT [DF_ProductImage_SortOrder] DEFAULT 1,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ProductImage_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ProductImage_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ProductImage] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ProductImage_SortOrder] CHECK ([SortOrder] BETWEEN 1 AND 5)
);
GO

-- Catalog
CREATE TABLE dbo.[ProductVariant] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ProductVariant_Id] DEFAULT NEWSEQUENTIALID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Sku] NVARCHAR(50) NOT NULL,
    [OptionText] NVARCHAR(100) NOT NULL,
    [PriceOverride] DECIMAL(18,2) NULL,
    [Stock] INT NOT NULL CONSTRAINT [DF_ProductVariant_Stock] DEFAULT 0,
    [ReservedStock] INT NOT NULL CONSTRAINT [DF_ProductVariant_ReservedStock] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductVariant_IsActive] DEFAULT 1,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ProductVariant_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ProductVariant_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ProductVariant] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ProductVariant_PriceOverride] CHECK ([PriceOverride] > 0),
    CONSTRAINT [CK_ProductVariant_Stock] CHECK ([Stock] >= 0),
    CONSTRAINT [CK_ProductVariant_ReservedStock] CHECK ([ReservedStock] >= 0),
    CONSTRAINT [CK_ProductVariant_ReservedWithinStock] CHECK ([ReservedStock] <= [Stock])
);
GO

-- Cart
CREATE TABLE dbo.[Cart] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Cart_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CouponId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Cart_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Cart_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Cart] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Cart
CREATE TABLE dbo.[CartItem] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CartItem_Id] DEFAULT NEWSEQUENTIALID(),
    [CartId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [VariantId] UNIQUEIDENTIFIER NULL,
    [Quantity] INT NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CartItem_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CartItem_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CartItem] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_CartItem_Quantity] CHECK ([Quantity] BETWEEN 1 AND 10)
);
GO

-- Cart
CREATE TABLE dbo.[WishlistItem] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WishlistItem_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_WishlistItem_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_WishlistItem_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_WishlistItem] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Orders
CREATE TABLE dbo.[CheckoutGroup] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CheckoutGroup_Id] DEFAULT NEWSEQUENTIALID(),
    [BuyerId] UNIQUEIDENTIFIER NOT NULL,
    [IdempotencyKey] NVARCHAR(64) NOT NULL,
    [ItemsTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_CheckoutGroup_ItemsTotal] DEFAULT 0,
    [DeliveryTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_CheckoutGroup_DeliveryTotal] DEFAULT 0,
    [DiscountTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_CheckoutGroup_DiscountTotal] DEFAULT 0,
    [CreditUsed] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_CheckoutGroup_CreditUsed] DEFAULT 0,
    [PayableTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_CheckoutGroup_PayableTotal] DEFAULT 0,
    [PaymentMethod] NVARCHAR(20) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ExpiresAt] DATETIME2(7) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CheckoutGroup_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CheckoutGroup_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CheckoutGroup] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_CheckoutGroup_ItemsTotal] CHECK ([ItemsTotal] >= 0),
    CONSTRAINT [CK_CheckoutGroup_DeliveryTotal] CHECK ([DeliveryTotal] >= 0),
    CONSTRAINT [CK_CheckoutGroup_DiscountTotal] CHECK ([DiscountTotal] >= 0),
    CONSTRAINT [CK_CheckoutGroup_CreditUsed] CHECK ([CreditUsed] >= 0),
    CONSTRAINT [CK_CheckoutGroup_PayableTotal] CHECK ([PayableTotal] >= 0),
    CONSTRAINT [CK_CheckoutGroup_PaymentMethod] CHECK ([PaymentMethod] IN ('COD', 'bKash', 'Nagad', 'Card')),
    CONSTRAINT [CK_CheckoutGroup_Status] CHECK ([Status] IN ('AwaitingPayment', 'Placed', 'Expired', 'Cancelled'))
);
GO

-- Orders
CREATE TABLE dbo.[Order] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Order_Id] DEFAULT NEWSEQUENTIALID(),
    [OrderNumber] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Order_OrderNumber] DEFAULT ('EM-' + CONVERT(CHAR(8), SYSUTCDATETIME(), 112) + '-' + RIGHT('000000' + CONVERT(VARCHAR(10), NEXT VALUE FOR dbo.OrderNumberSeq), 6)),
    [CheckoutGroupId] UNIQUEIDENTIFIER NOT NULL,
    [BuyerId] UNIQUEIDENTIFIER NOT NULL,
    [ShopId] UNIQUEIDENTIFIER NOT NULL,
    [CouponId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [ItemsTotal] DECIMAL(18,2) NOT NULL,
    [DeliveryFee] DECIMAL(18,2) NOT NULL,
    [DiscountAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Order_DiscountAmount] DEFAULT 0,
    [CreditUsed] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Order_CreditUsed] DEFAULT 0,
    [TotalAmount] DECIMAL(18,2) NOT NULL,
    [CommissionPercent] DECIMAL(5,2) NOT NULL,
    [CommissionAmount] DECIMAL(18,2) NOT NULL,
    [ShipName] NVARCHAR(100) NOT NULL,
    [ShipPhone] NVARCHAR(20) NOT NULL,
    [ShipAddress] NVARCHAR(400) NOT NULL,
    [ShipAreaId] UNIQUEIDENTIFIER NOT NULL,
    [CourierName] NVARCHAR(60) NULL,
    [TrackingNote] NVARCHAR(200) NULL,
    [DeliveredAt] DATETIME2(7) NULL,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Order_IsDeleted] DEFAULT 0,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Order_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Order_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Order_OrderNumber] CHECK ([OrderNumber] LIKE 'EM-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'),
    CONSTRAINT [CK_Order_Status] CHECK ([Status] IN ('AwaitingPayment', 'Pending', 'Confirmed', 'Processing', 'Shipped', 'Delivered', 'Cancelled')),
    CONSTRAINT [CK_Order_ItemsTotal] CHECK ([ItemsTotal] >= 0),
    CONSTRAINT [CK_Order_DeliveryFee] CHECK ([DeliveryFee] >= 0),
    CONSTRAINT [CK_Order_DiscountAmount] CHECK ([DiscountAmount] >= 0),
    CONSTRAINT [CK_Order_CreditUsed] CHECK ([CreditUsed] >= 0),
    CONSTRAINT [CK_Order_TotalAmount] CHECK ([TotalAmount] >= 0),
    CONSTRAINT [CK_Order_CommissionPercent] CHECK ([CommissionPercent] BETWEEN 0 AND 100),
    CONSTRAINT [CK_Order_CommissionAmount] CHECK ([CommissionAmount] >= 0)
);
GO

-- Orders
CREATE TABLE dbo.[OrderItem] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrderItem_Id] DEFAULT NEWSEQUENTIALID(),
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [VariantId] UNIQUEIDENTIFIER NULL,
    [ProductName] NVARCHAR(200) NOT NULL,
    [VariantText] NVARCHAR(100) NULL,
    [Sku] NVARCHAR(50) NULL,
    [UnitPrice] DECIMAL(18,2) NOT NULL,
    [Quantity] INT NOT NULL,
    [ReturnedQuantity] INT NOT NULL CONSTRAINT [DF_OrderItem_ReturnedQuantity] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderItem_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderItem_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_OrderItem] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_OrderItem_UnitPrice] CHECK ([UnitPrice] > 0),
    CONSTRAINT [CK_OrderItem_Quantity] CHECK ([Quantity] >= 1),
    CONSTRAINT [CK_OrderItem_ReturnedQuantity] CHECK ([ReturnedQuantity] >= 0),
    CONSTRAINT [CK_OrderItem_ReturnedWithinBought] CHECK ([ReturnedQuantity] <= [Quantity])
);
GO

-- Orders
CREATE TABLE dbo.[OrderStatusHistory] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrderStatusHistory_Id] DEFAULT NEWSEQUENTIALID(),
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [FromStatus] NVARCHAR(20) NULL,
    [ToStatus] NVARCHAR(20) NOT NULL,
    [ChangedById] UNIQUEIDENTIFIER NULL,
    [Note] NVARCHAR(300) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderStatusHistory_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OrderStatusHistory_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_OrderStatusHistory] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Orders
CREATE TABLE dbo.[ReturnRequest] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ReturnRequest_Id] DEFAULT NEWSEQUENTIALID(),
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [BuyerId] UNIQUEIDENTIFIER NOT NULL,
    [Reason] NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL,
    [PhotoPaths] NVARCHAR(1500) NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ReturnRequest_Status] DEFAULT 'Requested',
    [RejectionReason] NVARCHAR(500) NULL,
    [EscalatedAt] DATETIME2(7) NULL,
    [RefundMethod] NVARCHAR(20) NULL,
    [DecidedById] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReturnRequest_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReturnRequest_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ReturnRequest] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ReturnRequest_Reason] CHECK ([Reason] IN ('Damaged', 'WrongItem', 'NotAsDescribed', 'NotReceived', 'Other')),
    CONSTRAINT [CK_ReturnRequest_Status] CHECK ([Status] IN ('Requested', 'Approved', 'Rejected', 'ItemReceived', 'Refunded', 'Closed')),
    CONSTRAINT [CK_ReturnRequest_RefundMethod] CHECK ([RefundMethod] IN ('OriginalMethod', 'StoreCredit')),
    CONSTRAINT [CK_ReturnRequest_RejectionNeedsReason] CHECK ([Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL)
);
GO

-- Orders
CREATE TABLE dbo.[ReturnItem] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ReturnItem_Id] DEFAULT NEWSEQUENTIALID(),
    [ReturnRequestId] UNIQUEIDENTIFIER NOT NULL,
    [OrderItemId] UNIQUEIDENTIFIER NOT NULL,
    [Quantity] INT NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReturnItem_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReturnItem_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ReturnItem] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ReturnItem_Quantity] CHECK ([Quantity] >= 1)
);
GO

-- Catalog
CREATE TABLE dbo.[Review] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Review_Id] DEFAULT NEWSEQUENTIALID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [OrderItemId] UNIQUEIDENTIFIER NOT NULL,
    [BuyerId] UNIQUEIDENTIFIER NOT NULL,
    [Rating] INT NOT NULL,
    [Comment] NVARCHAR(1000) NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Review_Status] DEFAULT 'Visible',
    [SellerReply] NVARCHAR(500) NULL,
    [SellerRepliedAt] DATETIME2(7) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Review_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Review_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Review] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Review_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
    CONSTRAINT [CK_Review_Status] CHECK ([Status] IN ('Visible', 'Hidden'))
);
GO

-- Catalog
CREATE TABLE dbo.[ReviewReport] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ReviewReport_Id] DEFAULT NEWSEQUENTIALID(),
    [ReviewId] UNIQUEIDENTIFIER NOT NULL,
    [ReporterId] UNIQUEIDENTIFIER NOT NULL,
    [Reason] NVARCHAR(300) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ReviewReport_Status] DEFAULT 'Open',
    [DecidedById] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReviewReport_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ReviewReport_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ReviewReport] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ReviewReport_Status] CHECK ([Status] IN ('Open', 'Upheld', 'Dismissed'))
);
GO

-- Payments
CREATE TABLE dbo.[Payment] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Payment_Id] DEFAULT NEWSEQUENTIALID(),
    [CheckoutGroupId] UNIQUEIDENTIFIER NOT NULL,
    [BuyerId] UNIQUEIDENTIFIER NOT NULL,
    [Method] NVARCHAR(20) NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [TransactionId] NVARCHAR(100) NULL,
    [GatewayStatus] NVARCHAR(30) NULL,
    [PaidAt] DATETIME2(7) NULL,
    [LastCheckedAt] DATETIME2(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Payment_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Payment_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Payment] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Payment_Method] CHECK ([Method] IN ('COD', 'bKash', 'Nagad', 'Card')),
    CONSTRAINT [CK_Payment_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_Payment_Status] CHECK ([Status] IN ('Initiated', 'Unpaid', 'Paid', 'Failed', 'Cancelled', 'Expired', 'PartlyRefunded', 'Refunded'))
);
GO

-- Payments
CREATE TABLE dbo.[Refund] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Refund_Id] DEFAULT NEWSEQUENTIALID(),
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [ReturnRequestId] UNIQUEIDENTIFIER NULL,
    [OrderId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Method] NVARCHAR(20) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Refund_Status] DEFAULT 'Pending',
    [GatewayRefundId] NVARCHAR(100) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Refund_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Refund_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Refund] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Refund_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_Refund_Method] CHECK ([Method] IN ('OriginalMethod', 'StoreCredit')),
    CONSTRAINT [CK_Refund_Status] CHECK ([Status] IN ('Pending', 'Completed', 'Failed'))
);
GO

-- Payments
CREATE TABLE dbo.[IdempotencyKey] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_IdempotencyKey_Id] DEFAULT NEWSEQUENTIALID(),
    [Key] NVARCHAR(64) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Endpoint] NVARCHAR(100) NOT NULL,
    [RequestHash] NVARCHAR(64) NOT NULL,
    [ResponseStatus] INT NULL,
    [ResponseBody] NVARCHAR(4000) NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_IdempotencyKey_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_IdempotencyKey_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_IdempotencyKey] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Finance
CREATE TABLE dbo.[LedgerEntry] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_LedgerEntry_Id] DEFAULT NEWSEQUENTIALID(),
    [TransactionId] UNIQUEIDENTIFIER NOT NULL,
    [Account] NVARCHAR(30) NOT NULL,
    [ShopId] UNIQUEIDENTIFIER NULL,
    [UserId] UNIQUEIDENTIFIER NULL,
    [OrderId] UNIQUEIDENTIFIER NULL,
    [RefId] UNIQUEIDENTIFIER NULL,
    [Kind] NVARCHAR(30) NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Note] NVARCHAR(200) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_LedgerEntry_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_LedgerEntry_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_LedgerEntry] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_LedgerEntry_Account] CHECK ([Account] IN ('PlatformCash', 'SellerPending', 'SellerAvailable', 'SellerReserved', 'SellerPaidOut', 'Commission', 'Refunds', 'StoreCredit', 'Receivable')),
    CONSTRAINT [CK_LedgerEntry_Amount] CHECK ([Amount] <> 0)
);
GO

-- Finance
CREATE TABLE dbo.[Payout] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Payout_Id] DEFAULT NEWSEQUENTIALID(),
    [ShopId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(18,2) NOT NULL,
    [Method] NVARCHAR(20) NOT NULL,
    [AccountSnapshot] NVARCHAR(200) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Payout_Status] DEFAULT 'Requested',
    [ReferenceNo] NVARCHAR(100) NULL,
    [RejectionReason] NVARCHAR(500) NULL,
    [DecidedById] UNIQUEIDENTIFIER NULL,
    [DecidedAt] DATETIME2(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Payout_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Payout_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Payout] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Payout_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_Payout_Method] CHECK ([Method] IN ('BankTransfer', 'bKash', 'Nagad')),
    CONSTRAINT [CK_Payout_Status] CHECK ([Status] IN ('Requested', 'Paid', 'Rejected', 'Cancelled')),
    CONSTRAINT [CK_Payout_PaidNeedsReference] CHECK ([Status] <> 'Paid' OR [ReferenceNo] IS NOT NULL),
    CONSTRAINT [CK_Payout_RejectedNeedsReason] CHECK ([Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL)
);
GO

-- Promotions
CREATE TABLE dbo.[CouponRedemption] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CouponRedemption_Id] DEFAULT NEWSEQUENTIALID(),
    [CouponId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CheckoutGroupId] UNIQUEIDENTIFIER NOT NULL,
    [DiscountAmount] DECIMAL(18,2) NOT NULL,
    [ReleasedAt] DATETIME2(7) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CouponRedemption_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CouponRedemption_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CouponRedemption] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_CouponRedemption_DiscountAmount] CHECK ([DiscountAmount] >= 0)
);
GO

-- Promotions
CREATE TABLE dbo.[Banner] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Banner_Id] DEFAULT NEWSEQUENTIALID(),
    [Title] NVARCHAR(100) NOT NULL,
    [ImageUrl] NVARCHAR(300) NOT NULL,
    [LinkUrl] NVARCHAR(300) NULL,
    [SortOrder] INT NOT NULL CONSTRAINT [DF_Banner_SortOrder] DEFAULT 0,
    [StartsAt] DATETIME2(7) NULL,
    [EndsAt] DATETIME2(7) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Banner_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Banner_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Banner_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Banner] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Banner_DatesOrder] CHECK ([StartsAt] IS NULL OR [EndsAt] IS NULL OR [EndsAt] > [StartsAt])
);
GO

-- Platform services
CREATE TABLE dbo.[Notification] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Notification_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Type] NVARCHAR(40) NOT NULL,
    [Title] NVARCHAR(100) NOT NULL,
    [Message] NVARCHAR(400) NOT NULL,
    [LinkUrl] NVARCHAR(300) NULL,
    [IsRead] BIT NOT NULL CONSTRAINT [DF_Notification_IsRead] DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Notification_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Notification_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Notification] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Platform services
CREATE TABLE dbo.[OutboxMessage] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OutboxMessage_Id] DEFAULT NEWSEQUENTIALID(),
    [Type] NVARCHAR(100) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_OutboxMessage_Status] DEFAULT 'Pending',
    [Attempts] INT NOT NULL CONSTRAINT [DF_OutboxMessage_Attempts] DEFAULT 0,
    [NextAttemptAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OutboxMessage_NextAttemptAt] DEFAULT SYSUTCDATETIME(),
    [ProcessedAt] DATETIME2(7) NULL,
    [LastError] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OutboxMessage_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_OutboxMessage_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_OutboxMessage] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_OutboxMessage_Status] CHECK ([Status] IN ('Pending', 'Done', 'Failed')),
    CONSTRAINT [CK_OutboxMessage_Attempts] CHECK ([Attempts] >= 0)
);
GO

-- Platform services
CREATE TABLE dbo.[SystemSetting] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_SystemSetting_Id] DEFAULT NEWSEQUENTIALID(),
    [Key] NVARCHAR(60) NOT NULL,
    [Value] NVARCHAR(4000) NOT NULL,
    [ValueType] NVARCHAR(20) NOT NULL,
    [Description] NVARCHAR(200) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SystemSetting_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_SystemSetting_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SystemSetting] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_SystemSetting_ValueType] CHECK ([ValueType] IN ('Int', 'Decimal', 'Text', 'Html'))
);
GO

-- Platform services
CREATE TABLE dbo.[ContactMessage] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ContactMessage_Id] DEFAULT NEWSEQUENTIALID(),
    [Name] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(150) NOT NULL,
    [Subject] NVARCHAR(150) NOT NULL,
    [Message] NVARCHAR(2000) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ContactMessage_Status] DEFAULT 'New',
    [IpAddress] NVARCHAR(45) NULL,
    [HandledById] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ContactMessage_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_ContactMessage_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ContactMessage] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ContactMessage_Status] CHECK ([Status] IN ('New', 'Read', 'Closed'))
);
GO

/* ---------- ASP.NET Core Identity role tables (made by Identity, not counted in the 35) ---------- */
CREATE TABLE dbo.[AspNetRoles] (
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AspNetRoles_Id] DEFAULT NEWSEQUENTIALID(),
    [Name] NVARCHAR(256) NULL,
    [NormalizedName] NVARCHAR(256) NULL,
    [ConcurrencyStamp] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id])
);
GO
CREATE UNIQUE NONCLUSTERED INDEX [RoleNameIndex] ON dbo.[AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO
CREATE TABLE dbo.[AspNetRoleClaims] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [ClaimType] NVARCHAR(MAX) NULL,
    [ClaimValue] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES dbo.[AspNetRoles]([Id]) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX [IX_AspNetRoleClaims_RoleId] ON dbo.[AspNetRoleClaims] ([RoleId]);
GO
CREATE TABLE dbo.[AspNetUserRoles] (
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY CLUSTERED ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_User_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES dbo.[AspNetRoles]([Id]) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX [IX_AspNetUserRoles_RoleId] ON dbo.[AspNetUserRoles] ([RoleId]);
GO
CREATE TABLE dbo.[AspNetUserClaims] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ClaimType] NVARCHAR(MAX) NULL,
    [ClaimValue] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_User_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX [IX_AspNetUserClaims_UserId] ON dbo.[AspNetUserClaims] ([UserId]);
GO
CREATE TABLE dbo.[AspNetUserLogins] (
    [LoginProvider] NVARCHAR(128) NOT NULL,
    [ProviderKey] NVARCHAR(128) NOT NULL,
    [ProviderDisplayName] NVARCHAR(MAX) NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY CLUSTERED ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_User_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX [IX_AspNetUserLogins_UserId] ON dbo.[AspNetUserLogins] ([UserId]);
GO
CREATE TABLE dbo.[AspNetUserTokens] (
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [LoginProvider] NVARCHAR(128) NOT NULL,
    [Name] NVARCHAR(128) NOT NULL,
    [Value] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY CLUSTERED ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_User_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE
);
GO

/* ---------- Foreign keys (NO ACTION = Restrict; CASCADE only where SRS 6.4 allows) ---------- */
ALTER TABLE dbo.[Location] ADD CONSTRAINT [FK_Location_ParentId] FOREIGN KEY ([ParentId]) REFERENCES dbo.[Location]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[RefreshToken] ADD CONSTRAINT [FK_RefreshToken_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[RefreshToken] ADD CONSTRAINT [FK_RefreshToken_ReplacedByTokenId] FOREIGN KEY ([ReplacedByTokenId]) REFERENCES dbo.[RefreshToken]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[EmailToken] ADD CONSTRAINT [FK_EmailToken_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[Address] ADD CONSTRAINT [FK_Address_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[Address] ADD CONSTRAINT [FK_Address_DivisionId] FOREIGN KEY ([DivisionId]) REFERENCES dbo.[Location]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Address] ADD CONSTRAINT [FK_Address_DistrictId] FOREIGN KEY ([DistrictId]) REFERENCES dbo.[Location]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Address] ADD CONSTRAINT [FK_Address_AreaId] FOREIGN KEY ([AreaId]) REFERENCES dbo.[Location]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[AuditLog] ADD CONSTRAINT [FK_AuditLog_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[SellerApplication] ADD CONSTRAINT [FK_SellerApplication_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[SellerApplication] ADD CONSTRAINT [FK_SellerApplication_ReviewedById] FOREIGN KEY ([ReviewedById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Shop] ADD CONSTRAINT [FK_Shop_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Category] ADD CONSTRAINT [FK_Category_ParentId] FOREIGN KEY ([ParentId]) REFERENCES dbo.[Category]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Product] ADD CONSTRAINT [FK_Product_ShopId] FOREIGN KEY ([ShopId]) REFERENCES dbo.[Shop]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Product] ADD CONSTRAINT [FK_Product_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES dbo.[Category]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ProductImage] ADD CONSTRAINT [FK_ProductImage_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[ProductVariant] ADD CONSTRAINT [FK_ProductVariant_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Cart] ADD CONSTRAINT [FK_Cart_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[Cart] ADD CONSTRAINT [FK_Cart_CouponId] FOREIGN KEY ([CouponId]) REFERENCES dbo.[Coupon]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[CartItem] ADD CONSTRAINT [FK_CartItem_CartId] FOREIGN KEY ([CartId]) REFERENCES dbo.[Cart]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[CartItem] ADD CONSTRAINT [FK_CartItem_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[CartItem] ADD CONSTRAINT [FK_CartItem_VariantId] FOREIGN KEY ([VariantId]) REFERENCES dbo.[ProductVariant]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[WishlistItem] ADD CONSTRAINT [FK_WishlistItem_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[WishlistItem] ADD CONSTRAINT [FK_WishlistItem_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[CheckoutGroup] ADD CONSTRAINT [FK_CheckoutGroup_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Order] ADD CONSTRAINT [FK_Order_CheckoutGroupId] FOREIGN KEY ([CheckoutGroupId]) REFERENCES dbo.[CheckoutGroup]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Order] ADD CONSTRAINT [FK_Order_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Order] ADD CONSTRAINT [FK_Order_ShopId] FOREIGN KEY ([ShopId]) REFERENCES dbo.[Shop]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Order] ADD CONSTRAINT [FK_Order_CouponId] FOREIGN KEY ([CouponId]) REFERENCES dbo.[Coupon]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Order] ADD CONSTRAINT [FK_Order_ShipAreaId] FOREIGN KEY ([ShipAreaId]) REFERENCES dbo.[Location]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[OrderItem] ADD CONSTRAINT [FK_OrderItem_OrderId] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[OrderItem] ADD CONSTRAINT [FK_OrderItem_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[OrderItem] ADD CONSTRAINT [FK_OrderItem_VariantId] FOREIGN KEY ([VariantId]) REFERENCES dbo.[ProductVariant]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[OrderStatusHistory] ADD CONSTRAINT [FK_OrderStatusHistory_OrderId] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[OrderStatusHistory] ADD CONSTRAINT [FK_OrderStatusHistory_ChangedById] FOREIGN KEY ([ChangedById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReturnRequest] ADD CONSTRAINT [FK_ReturnRequest_OrderId] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReturnRequest] ADD CONSTRAINT [FK_ReturnRequest_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReturnRequest] ADD CONSTRAINT [FK_ReturnRequest_DecidedById] FOREIGN KEY ([DecidedById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReturnItem] ADD CONSTRAINT [FK_ReturnItem_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES dbo.[ReturnRequest]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[ReturnItem] ADD CONSTRAINT [FK_ReturnItem_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Review] ADD CONSTRAINT [FK_Review_ProductId] FOREIGN KEY ([ProductId]) REFERENCES dbo.[Product]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Review] ADD CONSTRAINT [FK_Review_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES dbo.[OrderItem]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Review] ADD CONSTRAINT [FK_Review_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReviewReport] ADD CONSTRAINT [FK_ReviewReport_ReviewId] FOREIGN KEY ([ReviewId]) REFERENCES dbo.[Review]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[ReviewReport] ADD CONSTRAINT [FK_ReviewReport_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[ReviewReport] ADD CONSTRAINT [FK_ReviewReport_DecidedById] FOREIGN KEY ([DecidedById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Payment] ADD CONSTRAINT [FK_Payment_CheckoutGroupId] FOREIGN KEY ([CheckoutGroupId]) REFERENCES dbo.[CheckoutGroup]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Payment] ADD CONSTRAINT [FK_Payment_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Refund] ADD CONSTRAINT [FK_Refund_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES dbo.[Payment]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Refund] ADD CONSTRAINT [FK_Refund_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES dbo.[ReturnRequest]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Refund] ADD CONSTRAINT [FK_Refund_OrderId] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[IdempotencyKey] ADD CONSTRAINT [FK_IdempotencyKey_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[LedgerEntry] ADD CONSTRAINT [FK_LedgerEntry_ShopId] FOREIGN KEY ([ShopId]) REFERENCES dbo.[Shop]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[LedgerEntry] ADD CONSTRAINT [FK_LedgerEntry_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[LedgerEntry] ADD CONSTRAINT [FK_LedgerEntry_OrderId] FOREIGN KEY ([OrderId]) REFERENCES dbo.[Order]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Payout] ADD CONSTRAINT [FK_Payout_ShopId] FOREIGN KEY ([ShopId]) REFERENCES dbo.[Shop]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Payout] ADD CONSTRAINT [FK_Payout_DecidedById] FOREIGN KEY ([DecidedById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[CouponRedemption] ADD CONSTRAINT [FK_CouponRedemption_CouponId] FOREIGN KEY ([CouponId]) REFERENCES dbo.[Coupon]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[CouponRedemption] ADD CONSTRAINT [FK_CouponRedemption_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[CouponRedemption] ADD CONSTRAINT [FK_CouponRedemption_CheckoutGroupId] FOREIGN KEY ([CheckoutGroupId]) REFERENCES dbo.[CheckoutGroup]([Id]) ON DELETE NO ACTION;
ALTER TABLE dbo.[Notification] ADD CONSTRAINT [FK_Notification_UserId] FOREIGN KEY ([UserId]) REFERENCES dbo.[User]([Id]) ON DELETE CASCADE;
ALTER TABLE dbo.[ContactMessage] ADD CONSTRAINT [FK_ContactMessage_HandledById] FOREIGN KEY ([HandledById]) REFERENCES dbo.[User]([Id]) ON DELETE NO ACTION;
GO

/* ---------- Indexes (every foreign key has one: DB-1) ---------- */
CREATE UNIQUE NONCLUSTERED INDEX [UX_Location_ParentId_Name] ON dbo.[Location] ([ParentId], [Name]);
CREATE NONCLUSTERED INDEX [IX_Location_ParentId_Level] ON dbo.[Location] ([ParentId], [Level]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_User_Email] ON dbo.[User] ([Email]);
CREATE NONCLUSTERED INDEX [IX_User_Status] ON dbo.[User] ([Status]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_RefreshToken_TokenHash] ON dbo.[RefreshToken] ([TokenHash]);
CREATE NONCLUSTERED INDEX [IX_RefreshToken_UserId] ON dbo.[RefreshToken] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_RefreshToken_ExpiresAt] ON dbo.[RefreshToken] ([ExpiresAt]);
CREATE NONCLUSTERED INDEX [IX_RefreshToken_ReplacedByTokenId] ON dbo.[RefreshToken] ([ReplacedByTokenId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_EmailToken_TokenHash] ON dbo.[EmailToken] ([TokenHash]);
CREATE NONCLUSTERED INDEX [IX_EmailToken_UserId] ON dbo.[EmailToken] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_Address_UserId] ON dbo.[Address] ([UserId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Address_UserId] ON dbo.[Address] ([UserId]) WHERE [IsDefault] = 1;
CREATE NONCLUSTERED INDEX [IX_Address_DivisionId] ON dbo.[Address] ([DivisionId]);
CREATE NONCLUSTERED INDEX [IX_Address_DistrictId] ON dbo.[Address] ([DistrictId]);
CREATE NONCLUSTERED INDEX [IX_Address_AreaId] ON dbo.[Address] ([AreaId]);
CREATE NONCLUSTERED INDEX [IX_AuditLog_UserId] ON dbo.[AuditLog] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_AuditLog_CreatedAt] ON dbo.[AuditLog] ([CreatedAt]);
CREATE NONCLUSTERED INDEX [IX_AuditLog_EntityName_EntityId] ON dbo.[AuditLog] ([EntityName], [EntityId]);
CREATE NONCLUSTERED INDEX [IX_SellerApplication_Status] ON dbo.[SellerApplication] ([Status]);
CREATE NONCLUSTERED INDEX [IX_SellerApplication_UserId] ON dbo.[SellerApplication] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_SellerApplication_ReviewedById] ON dbo.[SellerApplication] ([ReviewedById]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Shop_OwnerId] ON dbo.[Shop] ([OwnerId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Shop_Name] ON dbo.[Shop] ([Name]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Shop_Slug] ON dbo.[Shop] ([Slug]);
CREATE NONCLUSTERED INDEX [IX_Shop_Status] ON dbo.[Shop] ([Status]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Coupon_Code] ON dbo.[Coupon] ([Code]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Category_ParentId_Name] ON dbo.[Category] ([ParentId], [Name]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Category_Slug] ON dbo.[Category] ([Slug]);
CREATE NONCLUSTERED INDEX [IX_Product_CategoryId] ON dbo.[Product] ([CategoryId]);
CREATE NONCLUSTERED INDEX [IX_Product_ShopId] ON dbo.[Product] ([ShopId]);
CREATE NONCLUSTERED INDEX [IX_Product_Status] ON dbo.[Product] ([Status]);
CREATE NONCLUSTERED INDEX [IX_Product_Name] ON dbo.[Product] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Product_Price] ON dbo.[Product] ([Price]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ProductImage_ProductId_SortOrder] ON dbo.[ProductImage] ([ProductId], [SortOrder]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ProductVariant_ProductId_Sku] ON dbo.[ProductVariant] ([ProductId], [Sku]);
CREATE NONCLUSTERED INDEX [IX_ProductVariant_Sku] ON dbo.[ProductVariant] ([Sku]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Cart_UserId] ON dbo.[Cart] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_Cart_CouponId] ON dbo.[Cart] ([CouponId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_CartItem_CartId_ProductId_VariantId] ON dbo.[CartItem] ([CartId], [ProductId], [VariantId]);
CREATE NONCLUSTERED INDEX [IX_CartItem_ProductId] ON dbo.[CartItem] ([ProductId]);
CREATE NONCLUSTERED INDEX [IX_CartItem_VariantId] ON dbo.[CartItem] ([VariantId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_WishlistItem_UserId_ProductId] ON dbo.[WishlistItem] ([UserId], [ProductId]);
CREATE NONCLUSTERED INDEX [IX_WishlistItem_ProductId] ON dbo.[WishlistItem] ([ProductId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_CheckoutGroup_IdempotencyKey] ON dbo.[CheckoutGroup] ([IdempotencyKey]);
CREATE NONCLUSTERED INDEX [IX_CheckoutGroup_BuyerId] ON dbo.[CheckoutGroup] ([BuyerId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Order_OrderNumber] ON dbo.[Order] ([OrderNumber]);
CREATE NONCLUSTERED INDEX [IX_Order_BuyerId] ON dbo.[Order] ([BuyerId]);
CREATE NONCLUSTERED INDEX [IX_Order_ShopId] ON dbo.[Order] ([ShopId]);
CREATE NONCLUSTERED INDEX [IX_Order_Status] ON dbo.[Order] ([Status]);
CREATE NONCLUSTERED INDEX [IX_Order_CreatedAt] ON dbo.[Order] ([CreatedAt]);
CREATE NONCLUSTERED INDEX [IX_Order_CheckoutGroupId] ON dbo.[Order] ([CheckoutGroupId]);
CREATE NONCLUSTERED INDEX [IX_Order_CouponId] ON dbo.[Order] ([CouponId]);
CREATE NONCLUSTERED INDEX [IX_Order_ShipAreaId] ON dbo.[Order] ([ShipAreaId]);
CREATE NONCLUSTERED INDEX [IX_OrderItem_OrderId] ON dbo.[OrderItem] ([OrderId]);
CREATE NONCLUSTERED INDEX [IX_OrderItem_ProductId] ON dbo.[OrderItem] ([ProductId]);
CREATE NONCLUSTERED INDEX [IX_OrderItem_VariantId] ON dbo.[OrderItem] ([VariantId]);
CREATE NONCLUSTERED INDEX [IX_OrderStatusHistory_OrderId] ON dbo.[OrderStatusHistory] ([OrderId]);
CREATE NONCLUSTERED INDEX [IX_OrderStatusHistory_ChangedById] ON dbo.[OrderStatusHistory] ([ChangedById]);
CREATE NONCLUSTERED INDEX [IX_ReturnRequest_OrderId] ON dbo.[ReturnRequest] ([OrderId]);
CREATE NONCLUSTERED INDEX [IX_ReturnRequest_BuyerId] ON dbo.[ReturnRequest] ([BuyerId]);
CREATE NONCLUSTERED INDEX [IX_ReturnRequest_DecidedById] ON dbo.[ReturnRequest] ([DecidedById]);
CREATE NONCLUSTERED INDEX [IX_ReturnItem_ReturnRequestId] ON dbo.[ReturnItem] ([ReturnRequestId]);
CREATE NONCLUSTERED INDEX [IX_ReturnItem_OrderItemId] ON dbo.[ReturnItem] ([OrderItemId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Review_OrderItemId] ON dbo.[Review] ([OrderItemId]);
CREATE NONCLUSTERED INDEX [IX_Review_ProductId_Status] ON dbo.[Review] ([ProductId], [Status]);
CREATE NONCLUSTERED INDEX [IX_Review_BuyerId] ON dbo.[Review] ([BuyerId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ReviewReport_ReviewId_ReporterId] ON dbo.[ReviewReport] ([ReviewId], [ReporterId]);
CREATE NONCLUSTERED INDEX [IX_ReviewReport_ReporterId] ON dbo.[ReviewReport] ([ReporterId]);
CREATE NONCLUSTERED INDEX [IX_ReviewReport_DecidedById] ON dbo.[ReviewReport] ([DecidedById]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Payment_TransactionId] ON dbo.[Payment] ([TransactionId]) WHERE [TransactionId] IS NOT NULL;
CREATE NONCLUSTERED INDEX [IX_Payment_CheckoutGroupId] ON dbo.[Payment] ([CheckoutGroupId]);
CREATE NONCLUSTERED INDEX [IX_Payment_Status] ON dbo.[Payment] ([Status]);
CREATE NONCLUSTERED INDEX [IX_Payment_LastCheckedAt] ON dbo.[Payment] ([LastCheckedAt]);
CREATE NONCLUSTERED INDEX [IX_Payment_BuyerId] ON dbo.[Payment] ([BuyerId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Refund_GatewayRefundId] ON dbo.[Refund] ([GatewayRefundId]) WHERE [GatewayRefundId] IS NOT NULL;
CREATE NONCLUSTERED INDEX [IX_Refund_PaymentId] ON dbo.[Refund] ([PaymentId]);
CREATE NONCLUSTERED INDEX [IX_Refund_ReturnRequestId] ON dbo.[Refund] ([ReturnRequestId]);
CREATE NONCLUSTERED INDEX [IX_Refund_OrderId] ON dbo.[Refund] ([OrderId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_IdempotencyKey_Key_UserId] ON dbo.[IdempotencyKey] ([Key], [UserId]);
CREATE NONCLUSTERED INDEX [IX_IdempotencyKey_ExpiresAt] ON dbo.[IdempotencyKey] ([ExpiresAt]);
CREATE NONCLUSTERED INDEX [IX_IdempotencyKey_UserId] ON dbo.[IdempotencyKey] ([UserId]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_LedgerEntry_TransactionId_Account_RefId] ON dbo.[LedgerEntry] ([TransactionId], [Account], [RefId]);
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_ShopId_Account] ON dbo.[LedgerEntry] ([ShopId], [Account]);
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_UserId_Account] ON dbo.[LedgerEntry] ([UserId], [Account]);
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_OrderId] ON dbo.[LedgerEntry] ([OrderId]);
CREATE NONCLUSTERED INDEX [IX_Payout_ShopId] ON dbo.[Payout] ([ShopId]);
CREATE NONCLUSTERED INDEX [IX_Payout_Status] ON dbo.[Payout] ([Status]);
CREATE NONCLUSTERED INDEX [IX_Payout_DecidedById] ON dbo.[Payout] ([DecidedById]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_CouponRedemption_CheckoutGroupId] ON dbo.[CouponRedemption] ([CheckoutGroupId]);
CREATE NONCLUSTERED INDEX [IX_CouponRedemption_CouponId] ON dbo.[CouponRedemption] ([CouponId]);
CREATE NONCLUSTERED INDEX [IX_CouponRedemption_UserId] ON dbo.[CouponRedemption] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_Notification_UserId_IsRead_CreatedAt] ON dbo.[Notification] ([UserId], [IsRead], [CreatedAt]);
CREATE NONCLUSTERED INDEX [IX_OutboxMessage_Status_NextAttemptAt] ON dbo.[OutboxMessage] ([Status], [NextAttemptAt]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_SystemSetting_Key] ON dbo.[SystemSetting] ([Key]);
CREATE NONCLUSTERED INDEX [IX_ContactMessage_HandledById] ON dbo.[ContactMessage] ([HandledById]);
GO

/* ---------- DB-11: insert-only tables (no UPDATE, no DELETE for anyone) ---------- */
CREATE TRIGGER dbo.TR_LedgerEntry_InsertOnly ON dbo.[LedgerEntry] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51001, N'LedgerEntry accepts inserts only (DB-11). Book a reversing entry instead.', 1;
END
GO
CREATE TRIGGER dbo.TR_AuditLog_InsertOnly ON dbo.[AuditLog] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51002, N'AuditLog accepts inserts only (DB-11).', 1;
END
GO
CREATE TRIGGER dbo.TR_OrderStatusHistory_InsertOnly ON dbo.[OrderStatusHistory] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51003, N'OrderStatusHistory accepts inserts only (DB-11).', 1;
END
GO

/* ===================== SEED DATA (SRS 6.7) ===================== */
INSERT INTO dbo.[AspNetRoles] ([Name], [NormalizedName], [ConcurrencyStamp])
VALUES (N'Buyer', N'BUYER', CONVERT(NVARCHAR(36), NEWID())),
       (N'Seller', N'SELLER', CONVERT(NVARCHAR(36), NEWID())),
       (N'Admin', N'ADMIN', CONVERT(NVARCHAR(36), NEWID()));
GO
INSERT INTO dbo.[Category] ([Name], [Slug], [SortOrder]) VALUES
 (N'Electronics', N'electronics', 1), (N'Fashion', N'fashion', 2), (N'Home & Living', N'home-and-living', 3),
 (N'Grocery', N'grocery', 4), (N'Beauty', N'beauty', 5), (N'Books', N'books', 6);
GO
INSERT INTO dbo.[SystemSetting] ([Key], [Value], [ValueType], [Description]) VALUES
 (N'commissionPercent', N'10', N'Decimal', N'Default commission rate (%)'),
 (N'deliveryFee', N'60', N'Decimal', N'Default delivery fee (Tk)'),
 (N'freeDeliveryAbove', N'2000', N'Decimal', N'Free delivery limit (Tk)'),
 (N'returnWindowDays', N'7', N'Int', N'Days to ask a return and wait before earnings are available'),
 (N'minPayout', N'500', N'Decimal', N'Minimum payout request (Tk)'),
 (N'unpaidOrderCancelMinutes', N'30', N'Int', N'Payment window'),
 (N'lockoutMinutes', N'15', N'Int', N'Login lock time'),
 (N'failedLoginLimit', N'5', N'Int', N'Failed logins before lock'),
 (N'resetLinkMinutes', N'30', N'Int', N'Password reset link life'),
 (N'productsPerPage', N'12', N'Int', N'Products on one page'),
 (N'lowStockLimit', N'5', N'Int', N'Low stock alert level'),
 (N'sellerConfirmHours', N'48', N'Int', N'Seller confirmation timeout'),
 (N'codMaxAmount', N'20000', N'Decimal', N'Cash on Delivery maximum for one checkout (Tk)');
GO
INSERT INTO dbo.[Location] ([Level], [Name], [ParentId], [IsInsideDhaka]) VALUES
 ('Division', N'Barishal', NULL, 0),
 ('Division', N'Chattogram', NULL, 0),
 ('Division', N'Dhaka', NULL, 0),
 ('Division', N'Khulna', NULL, 0),
 ('Division', N'Mymensingh', NULL, 0),
 ('Division', N'Rajshahi', NULL, 0),
 ('Division', N'Rangpur', NULL, 0),
 ('Division', N'Sylhet', NULL, 0);
GO
INSERT INTO dbo.[Location] ([Level], [Name], [ParentId], [IsInsideDhaka])
SELECT 'District', v.District, p.[Id], 0
FROM (VALUES
 (N'Barishal', N'Barguna'),
 (N'Barishal', N'Barishal'),
 (N'Barishal', N'Bhola'),
 (N'Barishal', N'Jhalokati'),
 (N'Barishal', N'Patuakhali'),
 (N'Barishal', N'Pirojpur'),
 (N'Chattogram', N'Bandarban'),
 (N'Chattogram', N'Brahmanbaria'),
 (N'Chattogram', N'Chandpur'),
 (N'Chattogram', N'Chattogram'),
 (N'Chattogram', N'Cumilla'),
 (N'Chattogram', N'Cox''s Bazar'),
 (N'Chattogram', N'Feni'),
 (N'Chattogram', N'Khagrachhari'),
 (N'Chattogram', N'Lakshmipur'),
 (N'Chattogram', N'Noakhali'),
 (N'Chattogram', N'Rangamati'),
 (N'Dhaka', N'Dhaka'),
 (N'Dhaka', N'Faridpur'),
 (N'Dhaka', N'Gazipur'),
 (N'Dhaka', N'Gopalganj'),
 (N'Dhaka', N'Kishoreganj'),
 (N'Dhaka', N'Madaripur'),
 (N'Dhaka', N'Manikganj'),
 (N'Dhaka', N'Munshiganj'),
 (N'Dhaka', N'Narayanganj'),
 (N'Dhaka', N'Narsingdi'),
 (N'Dhaka', N'Rajbari'),
 (N'Dhaka', N'Shariatpur'),
 (N'Dhaka', N'Tangail'),
 (N'Khulna', N'Bagerhat'),
 (N'Khulna', N'Chuadanga'),
 (N'Khulna', N'Jashore'),
 (N'Khulna', N'Jhenaidah'),
 (N'Khulna', N'Khulna'),
 (N'Khulna', N'Kushtia'),
 (N'Khulna', N'Magura'),
 (N'Khulna', N'Meherpur'),
 (N'Khulna', N'Narail'),
 (N'Khulna', N'Satkhira'),
 (N'Mymensingh', N'Jamalpur'),
 (N'Mymensingh', N'Mymensingh'),
 (N'Mymensingh', N'Netrokona'),
 (N'Mymensingh', N'Sherpur'),
 (N'Rajshahi', N'Bogura'),
 (N'Rajshahi', N'Joypurhat'),
 (N'Rajshahi', N'Naogaon'),
 (N'Rajshahi', N'Natore'),
 (N'Rajshahi', N'Chapainawabganj'),
 (N'Rajshahi', N'Pabna'),
 (N'Rajshahi', N'Rajshahi'),
 (N'Rajshahi', N'Sirajganj'),
 (N'Rangpur', N'Dinajpur'),
 (N'Rangpur', N'Gaibandha'),
 (N'Rangpur', N'Kurigram'),
 (N'Rangpur', N'Lalmonirhat'),
 (N'Rangpur', N'Nilphamari'),
 (N'Rangpur', N'Panchagarh'),
 (N'Rangpur', N'Rangpur'),
 (N'Rangpur', N'Thakurgaon'),
 (N'Sylhet', N'Habiganj'),
 (N'Sylhet', N'Moulvibazar'),
 (N'Sylhet', N'Sunamganj'),
 (N'Sylhet', N'Sylhet')
) AS v(Division, District)
JOIN dbo.[Location] p ON p.[Level] = 'Division' AND p.[Name] = v.Division;
GO
/* Sample areas only (Dhaka city and Chattogram city). Load the full list of upazilas and areas from the
   official list before release (SRS 6.7). Areas inside Dhaka city get IsInsideDhaka = 1. */
INSERT INTO dbo.[Location] ([Level], [Name], [ParentId], [IsInsideDhaka])
SELECT 'Area', v.Area, d.[Id], CASE WHEN v.District = N'Dhaka' THEN 1 ELSE 0 END
FROM (VALUES
 (N'Dhaka', N'Dhanmondi'),
 (N'Dhaka', N'Gulshan'),
 (N'Dhaka', N'Mirpur'),
 (N'Dhaka', N'Uttara'),
 (N'Dhaka', N'Mohammadpur'),
 (N'Dhaka', N'Motijheel'),
 (N'Dhaka', N'Tejgaon'),
 (N'Dhaka', N'Banani'),
 (N'Chattogram', N'Kotwali'),
 (N'Chattogram', N'Pahartali'),
 (N'Chattogram', N'Panchlaish'),
 (N'Chattogram', N'Halishahar')
) AS v(District, Area)
JOIN dbo.[Location] d ON d.[Level] = 'District' AND d.[Name] = v.District;
GO
UPDATE dbo.[Location] SET [IsInsideDhaka] = 1 WHERE [Level] = 'District' AND [Name] = N'Dhaka';
GO

/* ===================== VERIFY (run these after the script) =====================
SELECT COUNT(*) AS Tables35 FROM INFORMATION_SCHEMA.TABLES
 WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME NOT LIKE 'AspNet%';          -- expect 35
SELECT COUNT(*) AS Divisions FROM dbo.Location WHERE [Level] = 'Division';    -- expect 8
SELECT COUNT(*) AS Districts FROM dbo.Location WHERE [Level] = 'District';    -- expect 64
SELECT [Name] FROM dbo.AspNetRoles;                                           -- Buyer, Seller, Admin

-- DB-12 nightly check: every ledger transaction must add up to 0 (expect no rows)
SELECT TransactionId, SUM(Amount) AS Total FROM dbo.LedgerEntry
GROUP BY TransactionId HAVING SUM(Amount) <> 0;
*/

/* ===================== NOTES =====================
 1. Column names are PascalCase (FullName, CreatedAt ...) so EF Core maps them without extra settings.
 2. Encrypted fields (NidNumber, PayoutAccount, AccountSnapshot, Shop.PayoutAccount) are plain NVARCHAR(200)
    here; the application encrypts the value before saving (DB-7).
 3. UpdatedAt has no trigger: EF Core / the DAL sets it on every change.
 4. Rules that a CHECK cannot express stay in the BLL: a category tree of 2 levels; one shop-wide unique SKU;
    refunds of a payment <= payment amount; payout minimum from SystemSetting; at most 20 cart lines.
 5. Two columns were added to SellerApplication because FR-3.2 asks for them but the data dictionary
    lists none: BusinessType and TradeLicencePath (optional). Remove them if you prefer the dictionary as is.
 6. The retention jobs of SRS 6.6 that delete old AuditLog rows need the insert-only trigger turned off for
    the job; use: DISABLE TRIGGER dbo.TR_AuditLog_InsertOnly ON dbo.AuditLog; ... ENABLE TRIGGER ...
 7. Hangfire creates its own tables in schema [HangFire] at first start.
=============================================================== */
PRINT N'EMarketDb ready.';
GO
SET NOEXEC OFF;
GO
