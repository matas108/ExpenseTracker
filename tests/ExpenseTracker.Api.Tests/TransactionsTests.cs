using System.Net;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

public class TransactionsTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Create_ReturnsTransactionMarkedManual_AndItAppearsInTheList()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);

        var created = await CreateTransactionAsync(client, account.Id, -42.35m, "2026-09-20", description: " Rimi ");
        var list = await ReadAsync<PagedResult<TransactionDto>>(await client.GetAsync("/api/transactions"));

        Assert.Equal(TransactionSource.Manual, created.Source);
        Assert.Equal("Rimi", created.Description);
        Assert.Equal(new DateOnly(2026, 9, 20), created.Date);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task Create_MissingAmountAndDate_Returns400_InsteadOfDefaultingToZero()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);

        var response = await PostAsync(client, "/api/transactions", new { accountId = account.Id });

        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Amount", errors);
        Assert.Contains("Date", errors);
    }

    [Fact]
    public async Task Create_UnknownAccountAndCategory_ReportsBoth()
    {
        var client = await NewUserClientAsync();

        var response = await PostAsync(client, "/api/transactions", new { accountId = 999999, categoryId = 999999, amount = -1, date = "2026-09-01" });

        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("AccountId", errors);
        Assert.Contains("CategoryId", errors);
    }

    [Fact]
    public async Task Update_CanMoveTransactionToAnotherAccountAndCategory()
    {
        var client = await NewUserClientAsync();
        var checking = await CreateAccountAsync(client, "Checking");
        var wallet = await CreateAccountAsync(client, "Wallet");
        var groceries = await CreateCategoryAsync(client, "Groceries");
        var transaction = await CreateTransactionAsync(client, checking.Id, -42.35m, "2026-09-20");

        var updated = await ReadAsync<TransactionDto>(await PutAsync(client, $"/api/transactions/{transaction.Id}",
            new { accountId = wallet.Id, categoryId = groceries.Id, amount = -40m, date = "2026-09-21", description = "Rimi (cash)" }));

        Assert.Equal((wallet.Id, groceries.Id, -40m), (updated.AccountId, updated.CategoryId, updated.Amount));
        Assert.Equal(TransactionSource.Manual, updated.Source);
    }

    [Fact]
    public async Task Delete_RemovesTransaction()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        var transaction = await CreateTransactionAsync(client, account.Id, -1m, "2026-09-01");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{transaction.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/transactions/{transaction.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_FiltersByAccountCategoryAndInclusiveDateRange()
    {
        var client = await NewUserClientAsync();
        var checking = await CreateAccountAsync(client, "Checking");
        var wallet = await CreateAccountAsync(client, "Wallet");
        var groceries = await CreateCategoryAsync(client, "Groceries");
        await CreateTransactionAsync(client, checking.Id, -1m, "2026-09-01");
        await CreateTransactionAsync(client, checking.Id, -2m, "2026-09-10", groceries.Id);
        await CreateTransactionAsync(client, checking.Id, -3m, "2026-09-20");
        await CreateTransactionAsync(client, wallet.Id, -4m, "2026-09-10", groceries.Id);

        async Task<decimal[]> Amounts(string query) =>
            (await ReadAsync<PagedResult<TransactionDto>>(await client.GetAsync($"/api/transactions?{query}")))
            .Items.Select(t => t.Amount).Order().ToArray();

        Assert.Equal([-3m, -2m, -1m], await Amounts($"accountId={checking.Id}"));
        Assert.Equal([-4m, -2m], await Amounts($"categoryId={groceries.Id}"));
        Assert.Equal([-4m, -3m, -2m], await Amounts("dateFrom=2026-09-10&dateTo=2026-09-20"));
        Assert.Equal([-2m], await Amounts($"accountId={checking.Id}&categoryId={groceries.Id}"));
    }

    [Fact]
    public async Task List_PagesNewestFirst()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        await CreateTransactionAsync(client, account.Id, -1m, "2026-09-01");
        await CreateTransactionAsync(client, account.Id, -3m, "2026-09-03");
        await CreateTransactionAsync(client, account.Id, -2m, "2026-09-02");

        var page1 = await ReadAsync<PagedResult<TransactionDto>>(await client.GetAsync("/api/transactions?page=1&pageSize=2"));
        var page2 = await ReadAsync<PagedResult<TransactionDto>>(await client.GetAsync("/api/transactions?page=2&pageSize=2"));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([-3m, -2m], page1.Items.Select(t => t.Amount));
        Assert.Equal([-1m], page2.Items.Select(t => t.Amount));
    }

    [Fact]
    public async Task CreatedAt_IsReturnedAsUtc_AfterARoundTripThroughTheDatabase()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        var created = await CreateTransactionAsync(client, account.Id, -1m, "2026-09-01");

        var fetched = await ReadAsync<TransactionDto>(await client.GetAsync($"/api/transactions/{created.Id}"));

        Assert.Equal(DateTimeKind.Utc, fetched.CreatedAt.Kind);
        Assert.Equal(created.CreatedAt, fetched.CreatedAt);
    }
}
