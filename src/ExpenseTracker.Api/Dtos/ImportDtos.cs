using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.Dtos;

public class ImportTransactionsForm
{
    // CSV with columns Date, Amount, Description and optionally Category, Account, ExternalId.
    [Required]
    public IFormFile File { get; set; } = default!;

    // Used for rows without an Account column/value.
    public int? AccountId { get; set; }
}

// Row is the 1-based record number in the file; the header is row 1.
public record ImportError(int Row, string Message);

public record ImportReport(int Imported, int Skipped, IReadOnlyList<ImportError> Errors);
