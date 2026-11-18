using System.Security.Claims;
using System.Text;
using EMarket.BLL.Identity;
using EMarket.Domain.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EMarket.Api.Infrastructure;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "EMarket";
    public string Audience { get; set; } = "EMarket.Web";
    public string Key { get; set; } = "";

    public SymmetricSecurityKey SigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(Key);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be set to at least 256 bits, in user-secrets or an environment variable.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

public sealed class JwtAccessTokenIssuer(JwtOptions options) : IAccessTokenIssuer
{
    public const string StampClaim = "stamp";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public AccessToken Issue(User user, IReadOnlyList<string> roles)
    {
        var expires = DateTime.UtcNow.Add(Lifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(StampClaim, user.SecurityStamp),
            new("mustChangePassword", user.MustChangePassword ? "true" : "false"),
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(options.SigningKey(), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}
