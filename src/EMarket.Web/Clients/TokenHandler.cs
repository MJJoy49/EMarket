using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EMarket.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EMarket.Web.Clients;

/// <summary>
/// Adds the bearer token to every API call and renews it shortly before it ends (AD-3, AD-4).
/// The tokens live only in the encrypted HttpOnly sign-in cookie, never in the page or in JavaScript (NFR-73).
/// </summary>
public sealed class TokenHandler(IHttpContextAccessor accessor, RefreshCoordinator refresher) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated == true)
        {
            var token = await refresher.EnsureFreshAsync(context, ct);
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        var correlation = context?.TraceIdentifier;
        if (correlation is not null && !request.Headers.Contains("X-Correlation-Id"))
        {
            request.Headers.Add("X-Correlation-Id", new string(correlation.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(40).ToArray()));
        }

        return await base.SendAsync(request, ct);
    }
}

/// <summary>
/// Refresh tokens work once (BR-47). Two page requests that arrive with the same old token must not both use it, or the API
/// would treat the second as a replay and end the session. The first one refreshes; the other reuses its answer.
/// </summary>
public sealed class RefreshCoordinator(IHttpClientFactory factory, ILogger<RefreshCoordinator> logger)
{
    public const string ClientName = "api-refresh";
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (TokenResponse Tokens, DateTime At)> recent = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string?> EnsureFreshAsync(HttpContext context, CancellationToken ct)
    {
        var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded)
        {
            return null;
        }

        var access = auth.Properties!.GetTokenValue("access_token");
        var refresh = auth.Properties.GetTokenValue("refresh_token");
        var expires = DateTime.TryParse(auth.Properties.GetTokenValue("expires_at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue;
        if (access is null || expires > DateTime.UtcNow.Add(Margin) || refresh is null)
        {
            return access;
        }

        var fresh = await RefreshAsync(refresh, ct);
        if (fresh is null)
        {
            // The refresh token is no longer good (logout elsewhere, block, password change): end this session.
            if (!context.Response.HasStarted)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return null;
        }

        Store(auth.Properties, fresh);
        if (!context.Response.HasStarted)
        {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, auth.Principal!, auth.Properties);
        }

        return fresh.AccessToken;
    }

    public static void Store(AuthenticationProperties properties, TokenResponse tokens) =>
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken },
            new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken },
            new AuthenticationToken { Name = "expires_at", Value = tokens.AccessTokenExpiresAt.ToString("o") },
        ]);

    private async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        await gate.WaitAsync(ct);
        try
        {
            foreach (var old in recent.Where(r => r.Value.At < DateTime.UtcNow.AddSeconds(-30)).Select(r => r.Key).ToList())
            {
                recent.TryRemove(old, out _);
            }

            if (recent.TryGetValue(key, out var done))
            {
                return done.Tokens;
            }

            using var response = await factory.CreateClient(ClientName).PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Refresh was refused with {Status}", (int)response.StatusCode);
                return null;
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (tokens is not null)
            {
                recent[key] = (tokens, DateTime.UtcNow);
            }

            return tokens;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }
}
