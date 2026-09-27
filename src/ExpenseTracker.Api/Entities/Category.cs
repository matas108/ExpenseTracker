namespace ExpenseTracker.Api.Entities;

public enum CategoryKind { Income, Expense }

public class Category
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public AppUser User { get; set; } = default!;
    public string Name { get; set; } = default!;
    public CategoryKind Kind { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
