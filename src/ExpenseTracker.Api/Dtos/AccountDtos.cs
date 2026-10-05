using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Api.Entities;

namespace ExpenseTracker.Api.Dtos;

public record AccountDto(
    int Id,
    string Name,
    AccountType Type,
    string Currency,
    decimal OpeningBalance,
    DateTime CreatedAt)
{
    public static AccountDto From(Account a) =>
        new(a.Id, a.Name, a.Type, a.Currency, a.OpeningBalance, a.CreatedAt);
}

// Balance = OpeningBalance + the sum of the account's transactions (expenses are negative).
public record AccountBalanceDto(
    int AccountId,
    string Currency,
    decimal OpeningBalance,
    decimal TransactionsTotal,
    decimal Balance);

// Used for both POST and PUT.
public record AccountRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] AccountType? Type,
    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code, e.g. EUR.")] string Currency,
    decimal OpeningBalance = 0);
