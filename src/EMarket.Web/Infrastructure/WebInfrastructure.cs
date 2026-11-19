using System.Security.Claims;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Auth;
using EMarket.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.Web;

/// <summary>Signs the user in with a cookie that holds the tokens, and keeps its life equal to the refresh token (NFR-32).</summary>
public static class SignInHelper
{
    public static async Task SignInAsync(HttpContext context, TokenResponse tokens, bool persistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, tokens.UserId.ToString()),
            new(ClaimTypes.Name, tokens.FullName),
            new("emailConfirmed", tokens.EmailConfirmed ? "true" : "false"),
            new("mustChangePassword", tokens.MustChangePassword ? "true" : "false"),
        };
        claims.AddRange(tokens.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var properties = new AuthenticationProperties { IsPersistent = persistent, ExpiresUtc = tokens.RefreshTokenExpiresAt, AllowRefresh = false };
        RefreshCoordinator.Store(properties, tokens);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), properties);
    }
}

/// <summary>Public site information for the footer (FR-21.2), cached for a minute.</summary>
public sealed class SiteInfo(ApiClient api, IMemoryCache cache)
{
    public async Task<IReadOnlyList<EMarket.Shared.Dtos.Catalog.CategoryDto>> CategoriesAsync() =>
        await cache.GetOrCreateAsync("category-menu", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60); // FR-5.10: cached for 60 seconds
            var result = await api.GetAsync<List<EMarket.Shared.Dtos.Catalog.CategoryDto>>("api/v1/categories");
            return (IReadOnlyList<EMarket.Shared.Dtos.Catalog.CategoryDto>)(result.Data ?? []);
        }) ?? [];

    public async Task<IReadOnlyDictionary<string, string>> GetAsync() =>
        await cache.GetOrCreateAsync("site-info", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var result = await api.GetAsync<Dictionary<string, string>>("api/v1/site");
            return (IReadOnlyDictionary<string, string>)(result.Data ?? new Dictionary<string, string>());
        }) ?? new Dictionary<string, string>();
}

/// <summary>Helpers shared by every page: result messages, API failures and form errors.</summary>
public abstract class AppPage : Page
{
    protected ApiClient Api => HttpContext.RequestServices.GetRequiredService<ApiClient>();

    public Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsSeller => User.IsInRole(Roles.Seller);

    public bool IsAdmin => User.IsInRole(Roles.Admin);

    protected void Success(string message) => TempData["Success"] = message;

    protected void Problem(string message) => TempData["Error"] = message;

    /// <summary>Shows the API's message and returns true when the call failed.</summary>
    protected bool Failed<T>(ApiResult<T> result)
    {
        if (result.Ok)
        {
            return false;
        }

        Problem(result.Message);
        return true;
    }

    /// <summary>Puts the API's field errors back on the form, so the user keeps what was typed (FR-22.4).</summary>
    protected void AddErrors<T>(ApiResult<T> result, string prefix = "Input")
    {
        foreach (var (field, messages) in result.Errors)
        {
            var name = field.Contains('.') ? field[(field.LastIndexOf('.') + 1)..] : field;
            foreach (var message in messages)
            {
                ModelState.AddModelError($"{prefix}.{name}", message);
            }
        }

        if (result.Errors.Count == 0)
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }
    }

    /// <summary>Goes back to the page the user came from, but only inside this site (never an address from the outside).</summary>
    protected IActionResult LocalRedirectOr(string? returnUrl, string fallbackPage) =>
        IsLocal(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToPage(fallbackPage);

    public static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != (char)92));
}

public static class Format
{
    private static readonly TimeSpan Bangladesh = TimeSpan.FromHours(6);

    /// <summary>NFR-65: money as Tk 1,234.50.</summary>
    public static string Tk(decimal amount) => $"Tk {amount:N2}";

    /// <summary>NFR-12 and NFR-65: the API gives UTC; screens show Bangladesh time as DD/MM/YYYY.</summary>
    public static string Date(DateTime utc) => utc.Add(Bangladesh).ToString("dd/MM/yyyy");

    public static string DateTime(DateTime utc) => utc.Add(Bangladesh).ToString("dd/MM/yyyy HH:mm");

    /// <summary>The API serves public images at /api/v1/media; the browser gets them from this app's /media route.</summary>
    public static string Media(string? apiUrl) => string.IsNullOrEmpty(apiUrl) ? "" : apiUrl.Replace("/api/v1/media/", "/media/", StringComparison.Ordinal);

    public static string Status(string status) => System.Text.RegularExpressions.Regex.Replace(status, "(?<=[a-z])(?=[A-Z])", " ");

    public static string Badge(string status) => status switch
    {
        "Delivered" or "Paid" or "Active" or "Approved" or "Completed" or "Refunded" or "Visible" => "text-bg-success",
        "Cancelled" or "Rejected" or "Failed" or "Expired" or "Blocked" or "Suspended" or "Hidden" or "Closed" => "text-bg-danger",
        "Pending" or "AwaitingPayment" or "Requested" or "Initiated" or "Unpaid" or "Draft" or "New" => "text-bg-warning",
        _ => "text-bg-secondary",
    };
}
