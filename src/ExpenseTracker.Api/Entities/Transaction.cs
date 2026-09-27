namespace ExpenseTracker.Api.Entities;

public enum TransactionSource { Manual, Csv, GoCardless }

public class Transaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public AppUser User { get; set; } = default!;
    public int AccountId { get; set; }
    public Account Account { get; set; } = default!;
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    // Negative for expense, positive for income.
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public TransactionSource Source { get; set; }

    // Used for dedup on import.
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
