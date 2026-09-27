using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Import;

// Thrown when the file as a whole can't be imported (as opposed to individual bad rows).
public class ImportFileException(string message) : Exception(message);

public class CsvTransactionImporter(AppDbContext db)
{
    public const int MaxRows = 10_000;

    // Day-first only: MM/dd vs dd/MM can't be told apart, so US-style dates are not accepted.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "yyyy.MM.dd"];

    private record Candidate(int Row, Transaction Transaction);

    public async Task<ImportReport> ImportAsync(string userId, Stream stream, int? defaultAccountId, CancellationToken ct)
    {
        var accountsByName = (await db.Accounts.Where(a => a.UserId == userId).ToListAsync(ct))
            .ToLookup(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var categoriesByName = (await db.Categories.Where(c => c.UserId == userId).ToListAsync(ct))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var errors = new List<ImportError>();
        var candidates = new List<Candidate>();

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        });

        try
        {
            if (!await csv.ReadAsync()) throw new ImportFileException("The file is empty.");
            csv.ReadHeader();
            var headers = csv.HeaderRecord!.Select(h => h.Trim().ToLowerInvariant()).ToHashSet();

            var missing = new[] { "date", "amount", "description" }.Where(h => !headers.Contains(h)).ToList();
            if (missing.Count > 0)
                throw new ImportFileException($"Missing required column(s): {string.Join(", ", missing)}.");
            if (!headers.Contains("account") && defaultAccountId is null)
                throw new ImportFileException("Add an Account column or pass accountId.");

            string? Field(string name) => headers.Contains(name) ? csv.GetField(name) : null;

            while (await csv.ReadAsync())
            {
                var row = csv.Parser.Row;
                if (row > MaxRows + 1) throw new ImportFileException($"The file has more than {MaxRows} rows.");

                var problems = new List<string>();

                if (!DateOnly.TryParseExact(Field("date"), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    problems.Add($"Invalid date '{Field("date")}' (use yyyy-MM-dd or dd.MM.yyyy).");

                if (!TryParseAmount(Field("amount"), out var amount))
                    problems.Add($"Invalid amount '{Field("amount")}'.");

                var description = Field("description") ?? "";
                if (description.Length > 500) problems.Add("Description is longer than 500 characters.");

                var externalId = NullIfEmpty(Field("externalid"));
                if (externalId?.Length > 200) problems.Add("ExternalId is longer than 200 characters.");

                int? accountId = defaultAccountId;
                var accountName = NullIfEmpty(Field("account"));
                if (accountName is not null)
                {
                    var matches = accountsByName[accountName].ToList();
                    if (matches.Count == 1) accountId = matches[0].Id;
                    else problems.Add(matches.Count == 0
                        ? $"Unknown account '{accountName}'."
                        : $"More than one account is named '{accountName}'.");
                }
                else if (accountId is null) problems.Add("No account given.");

                int? categoryId = null;
                var categoryName = NullIfEmpty(Field("category"));
                if (categoryName is not null)
                {
                    if (categoriesByName.TryGetValue(categoryName, out var category)) categoryId = category.Id;
                    else problems.Add($"Unknown category '{categoryName}'.");
                }

                if (problems.Count > 0)
                {
                    errors.Add(new ImportError(row, string.Join(" ", problems)));
                    continue;
                }

                candidates.Add(new Candidate(row, new Transaction
                {
                    UserId = userId,
                    AccountId = accountId!.Value,
                    CategoryId = categoryId,
                    Amount = amount,
                    Date = date,
                    Description = description,
                    Source = TransactionSource.Csv,
                    ExternalId = externalId,
                }));
            }
        }
        catch (CsvHelperException ex)
        {
            throw new ImportFileException($"Could not read the CSV near row {ex.Context?.Parser?.Row}: malformed data.");
        }

        AssignHashIds(candidates);

        var ids = candidates.Select(c => c.Transaction.ExternalId!).ToList();
        var existing = await db.Transactions
            .Where(t => t.UserId == userId && t.ExternalId != null && ids.Contains(t.ExternalId))
            .Select(t => t.ExternalId!)
            .ToListAsync(ct);

        var seen = existing.ToHashSet();
        var skipped = 0;
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.Transaction.ExternalId!)) { skipped++; continue; }
            db.Transactions.Add(candidate.Transaction);
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(candidates.Count - skipped, skipped, errors);
    }

    // Rows without an ExternalId get a hash of (account, date, amount, description) so re-importing
    // the same file skips them. The occurrence number keeps genuinely identical rows in one file
    // (two identical coffees on the same day) from being treated as duplicates of each other.
    private static void AssignHashIds(List<Candidate> candidates)
    {
        var occurrences = new Dictionary<string, int>();
        foreach (var t in candidates.Select(c => c.Transaction).Where(t => t.ExternalId is null))
        {
            var key = string.Join('|',
                t.AccountId,
                t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Amount.ToString("0.##", CultureInfo.InvariantCulture),
                t.Description.Trim().ToLowerInvariant());
            var n = occurrences[key] = occurrences.GetValueOrDefault(key) + 1;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{n}"));
            t.ExternalId = "csv:" + Convert.ToHexStringLower(hash)[..32];
        }
    }

    // Accepts "-42.35", "-42,35", "1,234.56", "1.234,56" and "1 234,56". The later of '.'/',' is the
    // decimal separator. More than 2 decimals is rejected, which also catches an ambiguous "1,234".
    internal static bool TryParseAmount(string? input, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Replace(" ", "").Replace(" ", "");
        s = s.LastIndexOf(',') > s.LastIndexOf('.')
            ? s.Replace(".", "").Replace(',', '.')
            : s.Replace(",", "");

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out amount)
               && amount.Scale <= 2;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
