using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ExpenseTracker.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    // Only valid behind [Authorize]; the JWT always carries "sub".
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");
}
