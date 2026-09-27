using ExpenseTracker.Api.Entities;

namespace ExpenseTracker.Api.Dtos;

// Income/expense are split by amount sign; TotalExpense is reported as a positive number.
public record SummaryDto(
    DateOnly? From,
    DateOnly? To,
    string? Currency,
    int TransactionCount,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Net,
    IReadOnlyList<CategorySummaryDto> ByCategory,
    IReadOnlyList<MonthSummaryDto> ByMonth);

// CategoryId/Name/Kind are null for uncategorised transactions. Total is signed.
public record CategorySummaryDto(int? CategoryId, string? Name, CategoryKind? Kind, decimal Total, int Count);

// Month is "yyyy-MM".
public record MonthSummaryDto(string Month, decimal Income, decimal Expense, decimal Net);
