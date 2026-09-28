using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

// Expected values are worked out by hand from samples/transactions.csv:
//   income  = 2500 (salary) + 120 (bike)                                          = 2620.00
//   expense = 850 + 42.35 + 3.50 + 3.50 + 61.20 + 19.99 + 27.80 + 35.10           = 1043.44
public class SummaryTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static async Task<SummaryDto> GetSummaryAsync(HttpClient client, string query = "") =>
        await ReadAsync<SummaryDto>(await client.GetAsync($"/api/summary{query}"));

    [Fact]
    public async Task Totals_MatchTheHandCalculation()
    {
        var client = await NewUserClientAsync();
        await SeedSampleAsync(client);

        var summary = await GetSummaryAsync(client);

        Assert.Equal("EUR", summary.Currency);
        Assert.Equal(10, summary.TransactionCount);
        Assert.Equal(2620.00m, summary.TotalIncome);
        Assert.Equal(1043.44m, summary.TotalExpense);
        Assert.Equal(1576.56m, summary.Net);
    }

    [Fact]
    public async Task ByCategory_HasSignedTotalsAndCounts_LargestFirst_WithAnUncategorisedBucket()
    {
        var client = await NewUserClientAsync();
        await SeedSampleAsync(client);

        var byCategory = (await GetSummaryAsync(client)).ByCategory
            .Select(c => (c.Name, c.Total, c.Count)).ToList();

        Assert.Equal(
        [
            ("Salary", 2500m, 1),
            ("Rent", -850m, 1),
            ("Groceries", -166.45m, 4),   // 42.35 + 61.20 + 27.80 + 35.10
            (null, 93.01m, 4),            // coffee x2, Spotify, bike sale
        ], byCategory);
    }

    [Fact]
    public async Task ByMonth_SplitsIncomeAndExpensePerMonth()
    {
        var client = await NewUserClientAsync();
        var account = await SeedSampleAsync(client);
        await CreateTransactionAsync(client, account.Id, -10m, "2026-10-02");

        var byMonth = (await GetSummaryAsync(client)).ByMonth
            .Select(m => (m.Month, m.Income, m.Expense, m.Net)).ToList();

        Assert.Equal([("2026-09", 2620m, 1043.44m, 1576.56m), ("2026-10", 0m, 10m, -10m)], byMonth);
    }

    [Fact]
    public async Task DateRange_IsInclusiveOnBothEnds()
    {
        var client = await NewUserClientAsync();
        await SeedSampleAsync(client);

        // 09-12 Spotify -19.99, 09-15 Lidl -27.80, 09-20 bike +120, 09-28 Rimi -35.10 (09-28 is the 'to' date)
        var summary = await GetSummaryAsync(client, "?from=2026-09-12&to=2026-09-28");

        Assert.Equal((4, 120m, 82.89m, 37.11m), (summary.TransactionCount, summary.TotalIncome, summary.TotalExpense, summary.Net));
    }

    [Fact]
    public async Task FromAfterTo_Returns400()
    {
        var client = await NewUserClientAsync();

        var response = await client.GetAsync("/api/summary?from=2026-10-01&to=2026-09-01");

        Assert.Contains("from", await ReadValidationErrorsAsync(response));
    }

    [Fact]
    public async Task MixedCurrencies_AreNeverAddedTogether()
    {
        var client = await NewUserClientAsync();
        await SeedSampleAsync(client);
        var usd = await CreateAccountAsync(client, "Revolut USD", "USD");
        await CreateTransactionAsync(client, usd.Id, -99m, "2026-09-15");

        var withoutCurrency = await client.GetAsync("/api/summary");
        var eur = await GetSummaryAsync(client, "?currency=eur");
        var usdSummary = await GetSummaryAsync(client, "?currency=USD");

        Assert.Contains("currency", await ReadValidationErrorsAsync(withoutCurrency));
        Assert.Equal(("EUR", 1043.44m), (eur.Currency, eur.TotalExpense));
        Assert.Equal(("USD", 1, 99m), (usdSummary.Currency, usdSummary.TransactionCount, usdSummary.TotalExpense));
    }

    [Fact]
    public async Task NoTransactions_ReturnsZeros()
    {
        var summary = await GetSummaryAsync(await NewUserClientAsync());

        Assert.Equal((0, 0m, 0m, 0m, (string?)null), (summary.TransactionCount, summary.TotalIncome, summary.TotalExpense, summary.Net, summary.Currency));
        Assert.Empty(summary.ByCategory);
        Assert.Empty(summary.ByMonth);
    }
}
