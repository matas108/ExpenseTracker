using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

public class AuthTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Tests_UseTheirOwnDatabase_NotTheDevDatabase()
    {
        await NewUserClientAsync();
        Assert.True(File.Exists(Factory.DbPath), "The API should be writing to the per-test SQLite file.");
    }

    [Fact]
    public async Task Register_ReturnsTokenThatAuthorizesRequests()
    {
        var client = AnonymousClient();
        var email = NewEmail();

        var auth = await ReadAsync<AuthResponse>(
            await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }), HttpStatusCode.Created);

        Assert.Equal(email, auth.Email);
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var client = AnonymousClient();
        var body = new { email = NewEmail(), password = Password };
        await client.PostAsJsonAsync("/api/auth/register", body);

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Contains("DuplicateUserName", await ReadValidationErrorsAsync(response));
    }

    [Fact]
    public async Task Register_WeakPassword_ReportsEachBrokenRule()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register", new { email = NewEmail(), password = "abc" });

        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("PasswordTooShort", errors);
        Assert.Contains("PasswordRequiresDigit", errors);
        Assert.Contains("PasswordRequiresUpper", errors);
    }

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsToken()
    {
        var client = AnonymousClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

        var auth = await ReadAsync<AuthResponse>(await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }));

        Assert.False(string.IsNullOrEmpty(auth.Token));
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownUser_BothReturn401()
    {
        var client = AnonymousClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong0rd!" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { email = NewEmail(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailedAttempts()
    {
        var client = AnonymousClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong0rd!" });
        var correctPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
    }

    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/categories")]
    [InlineData("/api/transactions")]
    [InlineData("/api/summary")]
    public async Task ProtectedEndpoints_WithoutToken_Return401(string url)
    {
        var response = await AnonymousClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedToken_Returns401()
    {
        var client = await NewUserClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^2] + "xx");

        var response = await client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
