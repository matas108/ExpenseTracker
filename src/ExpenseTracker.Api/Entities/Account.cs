namespace ExpenseTracker.Api.Entities;

public enum AccountType { Bank, Cash, Card, Other }

public class Account
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public AppUser User { get; set; } = default!;
    public string Name { get; set; } = default!;
    public AccountType Type { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal OpeningBalance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
