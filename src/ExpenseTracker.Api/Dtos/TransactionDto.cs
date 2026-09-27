using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Api.Entities;

namespace ExpenseTracker.Api.Dtos;

public record TransactionDto(
    int Id,
    int AccountId,
    int? CategoryId,
    decimal Amount,
    DateOnly Date,
    string Description,
    TransactionSource Source,
    string? ExternalId,
    DateTime CreatedAt)
{
    public static TransactionDto From(Transaction t) =>
        new(t.Id, t.AccountId, t.CategoryId, t.Amount, t.Date, t.Description, t.Source, t.ExternalId, t.CreatedAt);
}

// Used for both POST and PUT. Amount: negative for expense, positive for income.
public record TransactionRequest(
    [Required] int? AccountId,
    int? CategoryId,
    [Required] decimal? Amount,
    [Required] DateOnly? Date,
    [StringLength(500)] string? Description);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
