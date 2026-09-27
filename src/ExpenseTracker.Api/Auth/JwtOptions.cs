namespace ExpenseTracker.Api.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "ExpenseTracker";
    public string Audience { get; set; } = "ExpenseTracker";

    // HMAC-SHA256 signing key; must be at least 32 bytes.
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}
