using System.Net;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

// One user must never be able to see or change another user's data.
public class IsolationTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task OtherUsersRecords_Return404_ForReadUpdateAndDelete()
    {
        var alice = await NewUserClientAsync();
        var bob = await NewUserClientAsync();
        var account = await CreateAccountAsync(alice);
        var category = await CreateCategoryAsync(alice, "Groceries");
        var transaction = await CreateTransactionAsync(alice, account.Id, -10m, "2026-09-01", category.Id);

        var attempts = new[]
        {
            await bob.GetAsync($"/api/accounts/{account.Id}"),
            await PutAsync(bob, $"/api/accounts/{account.Id}", new { name = "Mine", type = "Cash", currency = "EUR" }),
            await bob.DeleteAsync($"/api/accounts/{account.Id}"),
            await bob.GetAsync($"/api/categories/{category.Id}"),
            await PutAsync(bob, $"/api/categories/{category.Id}", new { name = "Mine", kind = "Expense" }),
            await bob.DeleteAsync($"/api/categories/{category.Id}"),
            await bob.GetAsync($"/api/transactions/{transaction.Id}"),
            await PutAsync(bob, $"/api/transactions/{transaction.Id}", new { accountId = account.Id, amount = -1, date = "2026-09-01" }),
            await bob.DeleteAsync($"/api/transactions/{transaction.Id}"),
        };

        Assert.All(attempts, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(-10m, (await ReadAsync<TransactionDto>(await alice.GetAsync($"/api/transactions/{transaction.Id}"))).Amount);
    }

    [Fact]
    public async Task Lists_OnlyContainTheCallersData()
    {
        var alice = await NewUserClientAsync();
        var bob = await NewUserClientAsync();
        await SeedSampleAsync(alice);

        Assert.Empty(await ReadAsync<List<AccountDto>>(await bob.GetAsync("/api/accounts")));
        Assert.Empty(await ReadAsync<List<CategoryDto>>(await bob.GetAsync("/api/categories")));
        Assert.Equal(0, (await ReadAsync<PagedResult<TransactionDto>>(await bob.GetAsync("/api/transactions"))).TotalCount);
        Assert.Equal(0, (await ReadAsync<SummaryDto>(await bob.GetAsync("/api/summary"))).TransactionCount);
    }

    [Fact]
    public async Task CannotUseAnotherUsersAccountOrCategory_InATransaction()
    {
        var alice = await NewUserClientAsync();
        var bob = await NewUserClientAsync();
        var aliceAccount = await CreateAccountAsync(alice);
        var aliceCategory = await CreateCategoryAsync(alice, "Groceries");
        var bobAccount = await CreateAccountAsync(bob);

        var onAliceAccount = await PostAsync(bob, "/api/transactions", new { accountId = aliceAccount.Id, amount = -1, date = "2026-09-01" });
        var inAliceCategory = await PostAsync(bob, "/api/transactions",
            new { accountId = bobAccount.Id, categoryId = aliceCategory.Id, amount = -1, date = "2026-09-01" });

        Assert.Contains("AccountId", await ReadValidationErrorsAsync(onAliceAccount));
        Assert.Contains("CategoryId", await ReadValidationErrorsAsync(inAliceCategory));
    }

    [Fact]
    public async Task CannotImportIntoAnotherUsersAccount()
    {
        var alice = await NewUserClientAsync();
        var bob = await NewUserClientAsync();
        var aliceAccount = await CreateAccountAsync(alice);

        var response = await ImportAsync(bob, SampleCsv, aliceAccount.Id);

        Assert.Contains("AccountId", await ReadValidationErrorsAsync(response));
    }
}
