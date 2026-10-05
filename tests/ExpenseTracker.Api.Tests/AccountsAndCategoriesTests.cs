using System.Net;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

public class AccountsAndCategoriesTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task CreateAccount_TrimsNameUppercasesCurrencyAndReturnsLocation()
    {
        var client = await NewUserClientAsync();

        var response = await PostAsync(client, "/api/accounts",
            new { name = "  SEB checking ", type = "Bank", currency = "eur", openingBalance = 1200.50m });
        var account = await ReadAsync<AccountDto>(response, HttpStatusCode.Created);

        Assert.Equal("SEB checking", account.Name);
        Assert.Equal("EUR", account.Currency);
        Assert.Equal(AccountType.Bank, account.Type);
        Assert.Equal(1200.50m, account.OpeningBalance);
        Assert.Equal($"/api/accounts/{account.Id}", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public async Task CreateAccount_InvalidCurrency_Returns400(string currency)
    {
        var client = await NewUserClientAsync();

        var response = await PostAsync(client, "/api/accounts", new { name = "Cash", type = "Cash", currency });

        Assert.Contains("Currency", await ReadValidationErrorsAsync(response));
    }

    [Fact]
    public async Task UpdateAccount_ChangesAllFields()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client, "Cash");

        var updated = await ReadAsync<AccountDto>(await PutAsync(client, $"/api/accounts/{account.Id}",
            new { name = "Wallet", type = "Cash", currency = "USD", openingBalance = 40m }));

        Assert.Equal(("Wallet", AccountType.Cash, "USD", 40m), (updated.Name, updated.Type, updated.Currency, updated.OpeningBalance));
    }

    [Fact]
    public async Task DeleteAccount_WithTransactions_Returns409_UntilTheyAreRemoved()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        var transaction = await CreateTransactionAsync(client, account.Id, -5m, "2026-09-01");

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode);

        await client.DeleteAsync($"/api/transactions/{transaction.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{account.Id}")).StatusCode);
    }

    [Fact]
    public async Task CreateCategory_DuplicateName_Returns409()
    {
        var client = await NewUserClientAsync();
        await CreateCategoryAsync(client, "Groceries");

        var response = await PostAsync(client, "/api/categories", new { name = "Groceries", kind = "Expense" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SameCategoryName_IsAllowedForDifferentUsers()
    {
        await CreateCategoryAsync(await NewUserClientAsync(), "Groceries");
        await CreateCategoryAsync(await NewUserClientAsync(), "Groceries");
    }

    [Fact]
    public async Task RenameCategory_ToAnotherCategorysName_Returns409_ButKeepingItsOwnNameIsFine()
    {
        var client = await NewUserClientAsync();
        await CreateCategoryAsync(client, "Groceries");
        var salary = await CreateCategoryAsync(client, "Salary", "Income");

        var clash = await PutAsync(client, $"/api/categories/{salary.Id}", new { name = "Groceries", kind = "Income" });
        var sameName = await PutAsync(client, $"/api/categories/{salary.Id}", new { name = "Salary", kind = "Income" });

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sameName.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_KeepsItsTransactionsAsUncategorised()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        var category = await CreateCategoryAsync(client, "Groceries");
        var transaction = await CreateTransactionAsync(client, account.Id, -42.35m, "2026-09-03", category.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);

        var after = await ReadAsync<TransactionDto>(await client.GetAsync($"/api/transactions/{transaction.Id}"));
        Assert.Null(after.CategoryId);
        Assert.Equal(-42.35m, after.Amount);
    }

    [Fact]
    public async Task Balance_IsOpeningBalancePlusThisAccountsTransactions()
    {
        var client = await NewUserClientAsync();
        var checking = await ReadAsync<AccountDto>(await PostAsync(client, "/api/accounts",
            new { name = "Checking", type = "Bank", currency = "EUR", openingBalance = 1200.50m }), HttpStatusCode.Created);
        var wallet = await CreateAccountAsync(client, "Wallet");
        await CreateTransactionAsync(client, checking.Id, 2500m, "2026-09-01");
        await CreateTransactionAsync(client, checking.Id, -850m, "2026-09-02");
        await CreateTransactionAsync(client, checking.Id, -42.35m, "2026-09-03");
        await CreateTransactionAsync(client, wallet.Id, -99m, "2026-09-03"); // other account: must not count

        var balance = await ReadAsync<AccountBalanceDto>(await client.GetAsync($"/api/accounts/{checking.Id}/balance"));

        // 2500 - 850 - 42.35 = 1607.65, and 1200.50 + 1607.65 = 2808.15
        Assert.Equal(1607.65m, balance.TransactionsTotal);
        Assert.Equal(2808.15m, balance.Balance);
        Assert.Equal("EUR", balance.Currency);
    }

    [Fact]
    public async Task Balance_WithNoTransactions_IsTheOpeningBalance()
    {
        var client = await NewUserClientAsync();
        var account = await ReadAsync<AccountDto>(await PostAsync(client, "/api/accounts",
            new { name = "Savings", type = "Bank", currency = "EUR", openingBalance = 500m }), HttpStatusCode.Created);

        var balance = await ReadAsync<AccountBalanceDto>(await client.GetAsync($"/api/accounts/{account.Id}/balance"));

        Assert.Equal((0m, 500m), (balance.TransactionsTotal, balance.Balance));
    }

    [Fact]
    public async Task Balance_OfAnotherUsersAccount_Returns404()
    {
        var alice = await NewUserClientAsync();
        var bob = await NewUserClientAsync();
        var account = await CreateAccountAsync(alice);

        var response = await bob.GetAsync($"/api/accounts/{account.Id}/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
