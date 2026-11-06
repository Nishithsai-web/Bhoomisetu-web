using System.Security.Claims;
using Bhoomisetu.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bhoomisetu.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string TrustReviewerRole = "trust_reviewer";
    public const string RoleClaimType = "role";

    /// <summary>Base64 HMAC key, at least 32 bytes after decoding. From the secret store, never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "bhoomisetu";

    public string Audience { get; set; } = "bhoomisetu-web";

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(24);

    public SymmetricSecurityKey CreateKey()
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Auth:SigningKey must be base64.", ex);
        }

        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must decode to at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

public sealed class TokenService(AuthOptions options, IClock clock)
{
    private readonly SymmetricSecurityKey _key = options.CreateKey();

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, bool isTrustReviewer)
    {
        var now = clock.UtcNow;
        var expires = now + options.TokenLifetime;
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        if (isTrustReviewer)
        {
            claims.Add(new Claim(AuthOptions.RoleClaimType, AuthOptions.TrustReviewerRole));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's id. Endpoints using this require authorisation, so a missing claim is a server bug.</summary>
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated request has no valid subject claim.");
    }
}
