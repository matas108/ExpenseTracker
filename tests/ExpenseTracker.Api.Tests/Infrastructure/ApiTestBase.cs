using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseTracker.Api.Dtos;

namespace ExpenseTracker.Api.Tests.Infrastructure;

public abstract class ApiTestBase(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public const string Password = "Passw0rd!";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected ApiFactory Factory { get; } = factory;

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected static string NewEmail() => $"{Guid.NewGuid():N}@test.dev";

    // Each test registers its own user, so tests sharing a database never see each other's data.
    protected async Task<HttpClient> NewUserClientAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = NewEmail(), password = Password });
        var auth = await ReadAsync<AuthResponse>(response, HttpStatusCode.Created);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    // Reads a ValidationProblemDetails body and returns its error keys.
    protected static async Task<IReadOnlyCollection<string>> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<ValidationProblem>(response, HttpStatusCode.BadRequest);
        return problem.Errors.Keys;
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    protected static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, Json);

    protected static Task<HttpResponseMessage> PutAsync(HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, Json);

    protected static async Task<AccountDto> CreateAccountAsync(HttpClient client, string name = "Checking", string currency = "EUR") =>
        await ReadAsync<AccountDto>(
            await PostAsync(client, "/api/accounts", new { name, type = "Bank", currency }), HttpStatusCode.Created);

    protected static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, string name, string kind = "Expense") =>
        await ReadAsync<CategoryDto>(
            await PostAsync(client, "/api/categories", new { name, kind }), HttpStatusCode.Created);

    protected static async Task<TransactionDto> CreateTransactionAsync(
        HttpClient client, int accountId, decimal amount, string date, int? categoryId = null, string description = "") =>
        await ReadAsync<TransactionDto>(
            await PostAsync(client, "/api/transactions", new { accountId, categoryId, amount, date, description }),
            HttpStatusCode.Created);

    protected static Task<HttpResponseMessage> ImportAsync(HttpClient client, string csv, int? accountId = null) =>
        ImportAsync(client, Encoding.UTF8.GetBytes(csv), accountId);

    protected static Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] file, int? accountId = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(content, "file", "transactions.csv");
        if (accountId is not null) form.Add(new StringContent(accountId.Value.ToString()), "accountId");
        return client.PostAsync("/api/transactions/import", form);
    }

    protected static string SampleCsv => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "transactions.csv"));

    // Account + the categories used by samples/transactions.csv, then imports it.
    protected static async Task<AccountDto> SeedSampleAsync(HttpClient client)
    {
        var account = await CreateAccountAsync(client);
        await CreateCategoryAsync(client, "Salary", "Income");
        await CreateCategoryAsync(client, "Rent");
        await CreateCategoryAsync(client, "Groceries");
        var report = await ReadAsync<ImportReport>(await ImportAsync(client, SampleCsv, account.Id));
        Assert.Equal(10, report.Imported);
        return account;
    }
}
