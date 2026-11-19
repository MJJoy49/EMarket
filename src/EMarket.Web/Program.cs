using System.Security.Claims;
using EMarket.Shared.Constants;
using EMarket.Web;
using EMarket.Web.Clients;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);
var apiBase = new Uri((builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7001").TrimEnd('/') + "/");
var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services.AddTransient<TokenHandler>();
builder.Services.AddScoped<SiteInfo>();
builder.Services.AddHttpClient(RefreshCoordinator.ClientName, client =>
{
    client.BaseAddress = apiBase;
    client.Timeout = TimeSpan.FromSeconds(10);
});

// AD-7: outbound calls get a timeout, a circuit breaker and retries for safe (GET) calls only, so a payment or order is never repeated blindly.
builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = apiBase)
    .AddHttpMessageHandler<TokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.DisableForUnsafeHttpMethods();
        // A 503 is the API saying "under maintenance" on purpose (FR-21.9). It is not a fault to retry or to count against the breaker.
        options.Retry.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome));
        options.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome));
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/Error/403";
    options.Cookie.Name = "EMarket.Auth";
    options.Cookie.HttpOnly = true;                      // NFR-32
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = false;                   // the cookie ends with the refresh token
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()) // default deny; public pages say so below
    .AddPolicy("Shopper", policy => policy.RequireRole(Roles.Buyer, Roles.Seller))
    .AddPolicy("Seller", policy => policy.RequireRole(Roles.Seller))
    .AddPolicy("Admin", policy => policy.RequireRole(Roles.Admin));

builder.Services.AddRazorPages(options =>
{
    foreach (var page in new[] { "/Index", "/Account/Login", "/Account/Register", "/Account/ForgotPassword", "/Account/ResetPassword", "/Account/VerifyEmail", "/Account/ConfirmEmailChange" })
    {
        options.Conventions.AllowAnonymousToPage(page);
    }

    foreach (var folder in new[] { "/Products", "/Shops", "/Pages", "/Error", "/Maintenance" })
    {
        options.Conventions.AllowAnonymousToFolder(folder);
    }

    foreach (var folder in new[] { "/Cart", "/Wishlist", "/Checkout", "/Orders", "/Returns", "/Addresses", "/Reviews", "/BecomeSeller" })
    {
        options.Conventions.AuthorizeFolder(folder, "Shopper");
    }

    options.Conventions.AuthorizeAreaFolder("Seller", "/", "Seller");
    options.Conventions.AuthorizeAreaFolder("Admin", "/", "Admin");
});

var app = builder.Build();

app.UseExceptionHandler("/Error/500");           // FR-22.3: a friendly page with the correlation id, never a stack trace
app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseMiddleware<WebSecurityHeaders>();
app.UseMiddleware<MaintenanceRedirect>();
if (!isDevelopment)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
// With default-deny in place, an address that matches nothing would be sent to Login; answer 404 so the friendly 404 page shows.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    await next();
});
app.UseAuthentication();
app.UseMiddleware<MustChangePasswordRedirect>();
app.UseAuthorization();

// Public images and private files are fetched from the API with the user's own token, so the browser never needs the API address.
app.MapGet("/media/{**path}", async (string path, ApiClient api, CancellationToken ct) => await ProxyAsync(api, $"api/v1/media/{path}", cache: true, ct)).AllowAnonymous();
app.MapGet("/files/returns/{id:guid}/{index:int}", async (Guid id, int index, ApiClient api, CancellationToken ct) =>
    await ProxyAsync(api, $"api/v1/returns/{id}/photos/{index}", cache: false, ct));
app.MapGet("/files/nid/{id:guid}/{document}", async (Guid id, string document, ApiClient api, CancellationToken ct) =>
    await ProxyAsync(api, $"api/v1/admin/seller-applications/{id}/documents/{document}", cache: false, ct)).RequireAuthorization("Admin");

// Small JSON feed for the cart count and the unread badge, refreshed by site.js within 30 seconds (FR-14.2).
app.MapGet("/ui/counts", async (HttpContext context, ApiClient api, CancellationToken ct) =>
{
    var unread = await api.GetAsync<CountDto>("api/v1/notifications/unread-count", ct);
    var cart = context.User.IsInRole(Roles.Admin) ? null : await api.GetAsync<CountDto>("api/v1/cart/count", ct);
    return Results.Json(new { cart = cart?.Data?.Count ?? 0, unread = unread.Data?.Count ?? 0 });
});

app.MapRazorPages();
app.Run();

static bool IsTransient(Polly.Outcome<HttpResponseMessage> outcome) =>
    outcome.Exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException
    || outcome.Result is { StatusCode: var code } && (code == System.Net.HttpStatusCode.RequestTimeout
        || ((int)code >= 500 && code != System.Net.HttpStatusCode.ServiceUnavailable));

static async Task<IResult> ProxyAsync(ApiClient api, string path, bool cache, CancellationToken ct)
{
    var file = await api.DownloadAsync(path, ct);
    if (!file.Ok)
    {
        return Results.StatusCode(file.Status == 0 ? 503 : file.Status);
    }

    IResult result = Results.File(file.Data!.Content, file.Data.ContentType);
    return cache ? new CachedResult(result) : result;
}

public sealed record CountDto(int Count);

public partial class Program;
