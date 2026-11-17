# EMarket
```
EMarket/
├─ .github/                                        ← chore/repo-config ✔
│  ├─ workflows/ci.yml
│  └─ pull_request_template.md
├─ .gitattributes                                  ← chore/repo-config ✔
├─ .gitignore, README.md, EMarket.slnx             ← chore/project-setup ✔
├─ .editorconfig                                   ← chore/api-foundation
├─ Directory.Packages.props (সব NuGet সংস্করণ)       ← chore/api-foundation
├─ docker-compose.yml (MailHog, Redis)             ← feature/messaging-infra
├─ docs/                                           ← docs/final
│  ├─ EMarket_SRS_v2.0_final.pdf
│  ├─ database-schema.md
│  └─ deployment.md
│
├─ src/
│  │
│  ├─ EMarket.Domain/
│  │  ├─ Common/
│  │  │  └─ BaseEntity, AuditableEntity, ISoftDelete   ← feature/identity-foundation
│  │  ├─ Entities/
│  │  │  ├─ ApplicationUser.cs                       ← feature/identity-foundation
│  │  │  ├─ Setting.cs                               ← feature/settings-core
│  │  │  ├─ RefreshToken.cs                          ← feature/identity-login
│  │  │  ├─ OutboxMessage.cs                         ← feature/messaging-infra
│  │  │  ├─ Address.cs                               ← feature/profile
│  │  │  ├─ SellerApplication.cs                     ← feature/seller-application
│  │  │  ├─ Shop.cs                                  ← feature/seller-review
│  │  │  ├─ Category.cs                              ← feature/catalog-categories
│  │  │  ├─ Product, ProductVariant, ProductImage    ← feature/product-management
│  │  │  ├─ Cart, CartItem                           ← feature/cart
│  │  │  ├─ WishlistItem.cs                          ← feature/wishlist
│  │  │  ├─ Coupon, CouponRedemption                 ← feature/coupons
│  │  │  ├─ Order, OrderItem, OrderStatusHistory     ← feature/checkout
│  │  │  ├─ Payment.cs                               ← feature/payments
│  │  │  ├─ LedgerEntry.cs                           ← feature/ledger
│  │  │  ├─ ReturnRequest, Refund                    ← feature/returns-refunds
│  │  │  ├─ Review.cs                                ← feature/reviews
│  │  │  ├─ Notification.cs                          ← feature/notifications
│  │  │  ├─ Payout.cs                                ← feature/seller-earnings
│  │  │  ├─ AuditLog.cs                              ← feature/admin-users-shops
│  │  │  ├─ Banner.cs                                ← feature/banners
│  │  │  └─ ContactMessage.cs                        ← feature/static-contact
│  │  ├─ Enums/
│  │  │  ├─ RoleNames, UserStatus                    ← feature/identity-foundation
│  │  │  ├─ ApplicationStatus                        ← feature/seller-application
│  │  │  ├─ ProductStatus                            ← feature/product-management
│  │  │  ├─ OrderStatus                              ← feature/checkout
│  │  │  ├─ PaymentStatus, PaymentMethod             ← feature/payments
│  │  │  ├─ ReturnStatus, RefundStatus               ← feature/returns-refunds
│  │  │  └─ PayoutStatus                             ← feature/seller-earnings
│  │  └─ Events/ (MediatR ঘটনা)
│  │     ├─ UserRegistered                           ← feature/identity-email
│  │     ├─ SellerApproved, SellerRejected           ← feature/seller-review
│  │     ├─ ProductApproved, ProductRejected         ← feature/product-approval
│  │     ├─ OrderPlaced, OrderCancelled              ← feature/checkout
│  │     ├─ PaymentCompleted, PaymentFailed          ← feature/payments
│  │     └─ SettingChanged                           ← feature/system-settings
│  │
│  ├─ EMarket.Shared/
│  │  ├─ Constants/ Roles, Messages (MSG-xx), Rules  ← chore/api-foundation
│  │  ├─ Results/ Result, PagedResult                ← chore/api-foundation
│  │  └─ Dtos/
│  │     ├─ Auth/ RegisterRequest                    ← feature/identity-register
│  │     ├─ Auth/ LoginRequest, TokenResponse        ← feature/identity-login
│  │     ├─ Profile/ ProfileDto, AddressDto          ← feature/profile
│  │     ├─ Sellers/ ApplicationDto                  ← feature/seller-application
│  │     ├─ Sellers/ ReviewDecisionDto               ← feature/seller-review
│  │     ├─ Catalog/ CategoryDto                     ← feature/catalog-categories
│  │     ├─ Products/ ProductDto                     ← feature/product-management
│  │     ├─ Products/ ListItemDto, FilterDto         ← feature/browse-search
│  │     ├─ Cart/ CartDto                            ← feature/cart
│  │     ├─ Wishlist/ WishlistDto                    ← feature/wishlist
│  │     ├─ Coupons/ CouponDto                       ← feature/coupons
│  │     ├─ Checkout/ CheckoutRequest, Result        ← feature/checkout
│  │     ├─ Payments/ PaymentDto                     ← feature/payments
│  │     ├─ Orders/ OrderDto, ListItemDto            ← feature/orders-buyer, orders-seller
│  │     ├─ Returns/ ReturnDto, RefundDto            ← feature/returns-refunds
│  │     ├─ Reviews/ ReviewDto                       ← feature/reviews
│  │     ├─ Notifications/ NotificationDto           ← feature/notifications
│  │     ├─ Earnings/ EarningsDto, PayoutDto         ← feature/seller-earnings
│  │     ├─ Admin/ (User, Dashboard, Report, Setting)← প্রতিটা admin-* branch নিজেরটা আনে
│  │     └─ Contact/ ContactMessageDto               ← feature/static-contact
│  │
│  ├─ EMarket.DAL/
│  │  ├─ Data/
│  │  │  ├─ AppDbContext.cs                          ← feature/identity-foundation
│  │  │  └─ DbInitializer.cs (ভূমিকা, প্রথম admin)     ← feature/identity-foundation
│  │  ├─ Configurations/ (প্রতি entity-র ১টা ফাইল)      ← যে branch-এ entity আসে, সেখানেই
│  │  ├─ Repositories/
│  │  │  ├─ IRepository, Repository                  ← feature/identity-foundation
│  │  │  └─ ProductRepository, OrderRepository ...   ← যে branch-এ লাগে
│  │  ├─ Migrations/ (EF নিজে বানায়)                  ← প্রতি branch-এ ১টা
│  │  └─ Extensions/DalModule.cs                     ← feature/identity-foundation
│  │
│  ├─ EMarket.BLL/
│  │  ├─ Common/
│  │  │  ├─ Interfaces (ICurrentUser, IClock, IFileStorage) ← chore/api-foundation
│  │  │  ├─ Interfaces/IEmailSender                  ← feature/messaging-infra
│  │  │  └─ Behaviors (Validation, Logging)          ← chore/api-foundation
│  │  ├─ Identity/ AuthService, PasswordPolicy,
│  │  │            RegisterValidator                 ← feature/identity-register
│  │  ├─ Identity/ TokenService                      ← feature/identity-login
│  │  ├─ Identity/ EmailVerification, PasswordReset  ← feature/identity-email
│  │  ├─ Messaging/ OutboxProcessor, Handlers/       ← feature/messaging-infra
│  │  ├─ Settings/ SettingService (মান পড়া, ক্যাশ)     ← feature/settings-core
│  │  ├─ Profile/ ProfileService, AddressService     ← feature/profile
│  │  ├─ Sellers/ ApplicationService                 ← feature/seller-application
│  │  ├─ Sellers/ ReviewService                      ← feature/seller-review
│  │  ├─ Catalog/ CategoryService                    ← feature/catalog-categories
│  │  ├─ Products/ ProductService                    ← feature/product-management
│  │  ├─ Products/ ApprovalService                   ← feature/product-approval
│  │  ├─ Products/ SearchService                     ← feature/browse-search
│  │  ├─ Cart/ CartService                           ← feature/cart
│  │  ├─ Wishlist/ WishlistService                   ← feature/wishlist
│  │  ├─ Coupons/ CouponService                      ← feature/coupons
│  │  ├─ Checkout/ CheckoutService                   ← feature/checkout
│  │  ├─ Payments/ PaymentService, GatewayClient     ← feature/payments
│  │  ├─ Ledger/ LedgerService                       ← feature/ledger
│  │  ├─ Orders/ OrderService                        ← feature/orders-buyer
│  │  ├─ Orders/ SellerOrderService                  ← feature/orders-seller
│  │  ├─ Returns/ ReturnService, RefundService       ← feature/returns-refunds
│  │  ├─ Reviews/ ReviewService                      ← feature/reviews
│  │  ├─ Notifications/ NotificationService          ← feature/notifications
│  │  ├─ Earnings/ EarningsService                   ← feature/seller-earnings
│  │  ├─ Earnings/ PayoutService                     ← feature/admin-payouts
│  │  ├─ Admin/ UserAdminService, ShopAdminService,
│  │  │         AuditService                         ← feature/admin-users-shops
│  │  ├─ Reports/ ReportService                      ← feature/admin-reports
│  │  ├─ Contact/ ContactService                     ← feature/static-contact
│  │  ├─ Jobs/ OrderExpiryJob                        ← feature/payments
│  │  ├─ Jobs/ AutoCompleteJob                       ← feature/orders-seller
│  │  └─ Extensions/BllModule.cs                     ← chore/api-foundation
│  │
│  ├─ EMarket.Api/
│  │  ├─ Controllers/
│  │  │  ├─ AuthController (register)                ← feature/identity-register
│  │  │  ├─ AuthController (login, refresh, logout)  ← feature/identity-login
│  │  │  ├─ AuthController (verify, reset)           ← feature/identity-email
│  │  │  ├─ ProfileController, AddressesController   ← feature/profile
│  │  │  ├─ SellerApplicationsController             ← feature/seller-application
│  │  │  ├─ CategoriesController                     ← feature/catalog-categories
│  │  │  ├─ SellerProductsController                 ← feature/product-management
│  │  │  ├─ ProductsController (পাবলিক)               ← feature/browse-search
│  │  │  ├─ CartController                           ← feature/cart
│  │  │  ├─ WishlistController                       ← feature/wishlist
│  │  │  ├─ CheckoutController                       ← feature/checkout
│  │  │  ├─ PaymentsController (+ webhook)           ← feature/payments
│  │  │  ├─ OrdersController                         ← feature/orders-buyer
│  │  │  ├─ SellerOrdersController                   ← feature/orders-seller
│  │  │  ├─ ReturnsController                        ← feature/returns-refunds
│  │  │  ├─ ReviewsController                        ← feature/reviews
│  │  │  ├─ NotificationsController                  ← feature/notifications
│  │  │  ├─ EarningsController                       ← feature/seller-earnings
│  │  │  ├─ ContactController, PagesController       ← feature/static-contact
│  │  │  └─ Admin/
│  │  │     ├─ SellerReviewController                ← feature/seller-review
│  │  │     ├─ CategoryAdminController               ← feature/catalog-categories
│  │  │     ├─ ProductApprovalController             ← feature/product-approval
│  │  │     ├─ CouponAdminController                 ← feature/coupons
│  │  │     ├─ UserAdminController, ShopAdminController ← feature/admin-users-shops
│  │  │     ├─ OrderAdminController, PaymentAdminController ← feature/admin-orders-payments
│  │  │     ├─ PayoutAdminController                 ← feature/admin-payouts
│  │  │     ├─ BannerAdminController                 ← feature/banners
│  │  │     ├─ DashboardController, ReportController ← feature/admin-reports
│  │  │     └─ SettingsController                    ← feature/system-settings
│  │  ├─ Middleware/
│  │  │  ├─ ExceptionHandlingMiddleware              ← chore/api-foundation
│  │  │  ├─ RequestLoggingMiddleware                 ← chore/api-foundation
│  │  │  └─ MaintenanceModeMiddleware                ← feature/system-settings
│  │  ├─ Extensions/ SwaggerExtensions               ← chore/api-foundation
│  │  ├─ Extensions/ AuthExtensions (JWT)            ← feature/identity-login
│  │  ├─ Program.cs, appsettings.json (গোপন কিছু নয়)   ← chore/api-foundation
│  │  └─ Properties/launchSettings.json
│  │
│  └─ EMarket.Web/ (Razor Pages, শুধু Api-কে HTTP-তে ডাকে)
│     ├─ Clients/
│     │  ├─ ApiClientBase.cs                         ← feature/identity-register
│     │  ├─ TokenHandler.cs                          ← feature/identity-login
│     │  └─ ProductsClient, CartClient ...           ← যে branch-এ লাগে
│     ├─ Pages/
│     │  ├─ Shared/ _Layout, _ValidationScriptsPartial ← feature/identity-register
│     │  ├─ Account/Register                         ← feature/identity-register
│     │  ├─ Account/Login, Logout                    ← feature/identity-login
│     │  ├─ Account/ForgotPassword, ResetPassword,
│     │  │          VerifyEmail, ChangePassword      ← feature/identity-email
│     │  ├─ Profile/, Addresses/                     ← feature/profile
│     │  ├─ BecomeSeller/ Apply, Status              ← feature/seller-application
│     │  ├─ Index (হোম), Products/ Index, Details    ← feature/browse-search
│     │  ├─ Cart/                                    ← feature/cart
│     │  ├─ Wishlist/                                ← feature/wishlist
│     │  ├─ Checkout/ Index, Success, PaymentFailed  ← feature/checkout, feature/payments
│     │  ├─ Orders/ Index, Details, Track            ← feature/orders-buyer
│     │  ├─ Returns/ Request, Status                 ← feature/returns-refunds
│     │  ├─ Reviews/ Create                          ← feature/reviews
│     │  ├─ Notifications/                           ← feature/notifications
│     │  ├─ Static/ About, Terms, Privacy, FAQ, Contact ← feature/static-contact
│     │  ├─ Error/                                   ← feature/static-contact
│     │  └─ Maintenance/                             ← feature/system-settings
│     ├─ Areas/
│     │  ├─ Seller/Pages/
│     │  │  ├─ Products/ (তালিকা, নতুন, সম্পাদনা)       ← feature/product-management
│     │  │  ├─ Orders/                               ← feature/orders-seller
│     │  │  └─ Index (Dashboard), Earnings/          ← feature/seller-earnings
│     │  └─ Admin/Pages/
│     │     ├─ Shared/_AdminLayout, Index (ফাঁকা)     ← feature/admin-shell
│     │     ├─ Sellers/ (আবেদন তালিকা, যাচাই)         ← feature/seller-review
│     │     ├─ Categories/                           ← feature/catalog-categories
│     │     ├─ Products/ (approval queue)            ← feature/product-approval
│     │     ├─ Coupons/                              ← feature/coupons
│     │     ├─ Users/                                ← feature/admin-users-shops
│     │     ├─ Orders/, Payments/                    ← feature/admin-orders-payments
│     │     ├─ Payouts/                              ← feature/admin-payouts
│     │     ├─ Banners/                              ← feature/banners
│     │     ├─ Index (সত্যিকারের Dashboard), Reports/ ← feature/admin-reports
│     │     └─ Settings/                             ← feature/system-settings
│     ├─ wwwroot/ css, js, lib, images               ← chore/api-foundation
│     └─ Program.cs, appsettings.json
│
└─ tests/EMarket.Tests/
   ├─ TestUtils/                                     ← feature/identity-foundation
   ├─ Identity/ RegisterTests                        ← feature/identity-register
   ├─ Identity/ LoginTests, TokenTests               ← feature/identity-login
   ├─ Sellers/                                       ← feature/seller-application, seller-review
   ├─ Products/                                      ← feature/product-management, browse-search
   ├─ Cart/ Wishlist/ Coupons/                       ← feature/cart, wishlist, coupons
   ├─ Checkout/ Payments/ Ledger/                    ← feature/checkout, payments, ledger
   ├─ Orders/ Returns/                               ← feature/orders-*, returns-refunds
   ├─ Admin/ Reports/ Settings/                      ← প্রতিটা admin-* branch নিজেরটা
   └─ Integration/ (আসল DB দিয়ে, Testcontainers)      ← chore/hardening


   ```