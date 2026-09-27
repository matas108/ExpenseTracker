using System.Security.Claims;
using System.Text;
using ExpenseTracker.Api.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Auth;

public class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _jwt = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(AppUser user)
    {
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(_jwt.ExpiryMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
