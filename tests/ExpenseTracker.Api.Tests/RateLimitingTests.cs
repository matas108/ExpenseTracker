using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace ExpenseTracker.Api.Tests;

public class RateLimitingTests(RateLimitingTests.LowLimitFactory factory) : IClassFixture<RateLimitingTests.LowLimitFactory>
{
    // Same test app as everywhere else, but only 3 auth requests per minute.
    public class LowLimitFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:AuthPermitLimit", "3");
        }
    }

    [Fact]
    public async Task AuthEndpoints_Return429_OnceTheLimitIsUsedUp_OtherEndpointsAreUnaffected()
    {
        var client = factory.CreateClient();
        var login = new { email = "nobody@test.dev", password = "Wrong0rd!" };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email = "new@test.dev", password = "Passw0rd!" });
        var accounts = await client.GetAsync("/api/accounts");

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, register.StatusCode); // same limit for the whole controller
        Assert.Equal(HttpStatusCode.Unauthorized, accounts.StatusCode);    // not rate limited, just needs a token
    }
}