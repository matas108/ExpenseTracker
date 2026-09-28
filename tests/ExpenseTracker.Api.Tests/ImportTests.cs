using System.Text;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using ExpenseTracker.Api.Tests.Infrastructure;

namespace ExpenseTracker.Api.Tests;

public class ImportTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<List<TransactionDto>> AllTransactionsAsync(HttpClient client) =>
        (await ReadAsync<PagedResult<TransactionDto>>(await client.GetAsync("/api/transactions?pageSize=200"))).Items.ToList();

    [Fact]
    public async Task SampleFile_ImportsEveryRow_AndReimportingSkipsEveryRow()
    {
        var client = await NewUserClientAsync();
        var account = await SeedSampleAsync(client); // first import asserted inside

        var again = await ReadAsync<ImportReport>(await ImportAsync(client, SampleCsv, account.Id));

        Assert.Equal((0, 10), (again.Imported, again.Skipped));
        Assert.Empty(again.Errors);
        Assert.Equal(10, (await AllTransactionsAsync(client)).Count);
    }

    [Fact]
    public async Task ImportedRows_KeepValuesAndAreMarkedCsv()
    {
        var client = await NewUserClientAsync();
        await SeedSampleAsync(client);

        var transactions = await AllTransactionsAsync(client);
        var spotify = Assert.Single(transactions, t => t.Description == "Spotify, family plan"); // quoted comma
        var rent = Assert.Single(transactions, t => t.Description == "Rent September");

        Assert.All(transactions, t => Assert.Equal(TransactionSource.Csv, t.Source));
        Assert.Equal((-19.99m, new DateOnly(2026, 9, 12), (int?)null), (spotify.Amount, spotify.Date, spotify.CategoryId));
        Assert.NotNull(rent.CategoryId);
    }

    [Fact]
    public async Task IdenticalRowsInOneFile_AreBothImported()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        const string csv = "Date,Amount,Description\n2026-09-05,-3.50,Coffee\n2026-09-05,-3.50,Coffee\n";

        var report = await ReadAsync<ImportReport>(await ImportAsync(client, csv, account.Id));

        Assert.Equal((2, 0), (report.Imported, report.Skipped));
    }

    [Fact]
    public async Task SameFile_IntoADifferentAccount_IsNotADuplicate()
    {
        var client = await NewUserClientAsync();
        var checking = await CreateAccountAsync(client, "Checking");
        var wallet = await CreateAccountAsync(client, "Wallet");
        const string csv = "Date,Amount,Description\n2026-09-05,-3.50,Coffee\n";

        await ImportAsync(client, csv, checking.Id);
        var report = await ReadAsync<ImportReport>(await ImportAsync(client, csv, wallet.Id));

        Assert.Equal(1, report.Imported);
    }

    [Fact]
    public async Task DuplicateExternalId_IsSkipped_EvenWithDifferentDetails()
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);
        const string csv = "Date,Amount,Description,ExternalId\n2026-09-28,100,Bonus,BANK-777\n2026-09-29,100,Bonus again,BANK-777\n";

        var report = await ReadAsync<ImportReport>(await ImportAsync(client, csv, account.Id));

        Assert.Equal((1, 1), (report.Imported, report.Skipped));
        Assert.Equal("BANK-777", Assert.Single(await AllTransactionsAsync(client)).ExternalId);
    }

    [Fact]
    public async Task EuropeanBankExport_SemicolonsCommaDecimalsDayFirstDatesAndBom()
    {
        var client = await NewUserClientAsync();
        var wallet = await CreateAccountAsync(client, "Wallet");
        await CreateCategoryAsync(client, "Groceries");
        var csv = "DATE; Amount ;description;ACCOUNT;category\n28.09.2026;-12,40;Iki;wallet;groceries\n28.09.2026;1.234,56;Bonus;Wallet;\n";
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        var report = await ReadAsync<ImportReport>(await ImportAsync(client, withBom));

        Assert.Equal(2, report.Imported);
        Assert.Empty(report.Errors);
        var amounts = (await AllTransactionsAsync(client)).Select(t => (t.AccountId, t.Amount, t.Date)).OrderBy(t => t.Amount);
        Assert.Equal([(wallet.Id, -12.40m, new DateOnly(2026, 9, 28)), (wallet.Id, 1234.56m, new DateOnly(2026, 9, 28))], amounts);
    }

    [Fact]
    public async Task BadRows_AreReportedWithRowNumbers_WhileGoodRowsAreImported()
    {
        var client = await NewUserClientAsync();
        await CreateAccountAsync(client, "Wallet");
        var csv = string.Join('\n',
            "Date,Amount,Description,Account,Category",
            "2026-09-28,-5,Good row,Wallet,",               // row 2
            "2026-13-01,abc,Bad date and amount,Wallet,",   // row 3
            "2026-09-28,-5,No such account,Revolut,",       // row 4
            "2026-09-28,-5,No such category,Wallet,Pets",   // row 5
            "09/28/2026,-5,US date,Wallet,",                // row 6
            "2026-09-28,\"1,234\",Ambiguous amount,Wallet,", // row 7
            "2026-09-28,-5,No account at all,,");            // row 8

        var report = await ReadAsync<ImportReport>(await ImportAsync(client, csv));

        Assert.Equal(1, report.Imported);
        Assert.Equal([3, 4, 5, 6, 7, 8], report.Errors.Select(e => e.Row));
        Assert.Contains("Invalid date", report.Errors[0].Message);
        Assert.Contains("Invalid amount", report.Errors[0].Message); // every problem on a row is reported
        Assert.Contains("Revolut", report.Errors[1].Message);
        Assert.Contains("Pets", report.Errors[2].Message);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("Date,Amount\n2026-09-01,5\n", "description")]
    [InlineData("Date,Amount,Description\n2026-09-01,5,\"unterminated\n", "malformed")]
    public async Task UnreadableFiles_Return400_AndImportNothing(string csv, string expectedMessage)
    {
        var client = await NewUserClientAsync();
        var account = await CreateAccountAsync(client);

        var response = await ImportAsync(client, csv, account.Id);

        var problem = await ReadAsync<Problem>(response, System.Net.HttpStatusCode.BadRequest);
        Assert.Contains(expectedMessage, problem.Errors["File"][0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await AllTransactionsAsync(client));
    }

    [Fact]
    public async Task NoAccountColumnAndNoAccountId_Returns400()
    {
        var client = await NewUserClientAsync();

        var response = await ImportAsync(client, "Date,Amount,Description\n2026-09-01,5,x\n");

        Assert.Contains("File", await ReadValidationErrorsAsync(response));
    }

    private record Problem(Dictionary<string, string[]> Errors);
}
