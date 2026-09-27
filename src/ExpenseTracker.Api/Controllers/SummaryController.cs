using ExpenseTracker.Api.Auth;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/summary")]
public class SummaryController(AppDbContext db) : ControllerBase
{
    private record Row(decimal Amount, DateOnly Date, int? CategoryId, string? CategoryName, CategoryKind? CategoryKind, string Currency);

    /// <summary>Totals for a date range (inclusive). Amounts in different currencies are never added together.</summary>
    [HttpGet]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SummaryDto>> Get(DateOnly? from, DateOnly? to, string? currency)
    {
        if (from > to)
        {
            ModelState.AddModelError(nameof(from), "'from' must be on or before 'to'.");
            return ValidationProblem(ModelState);
        }

        var userId = User.GetUserId();
        var query = db.Transactions.AsNoTracking().Where(t => t.UserId == userId);
        if (from is not null) query = query.Where(t => t.Date >= from);
        if (to is not null) query = query.Where(t => t.Date <= to);
        if (currency is not null)
        {
            currency = currency.ToUpperInvariant();
            query = query.Where(t => t.Account.Currency == currency);
        }

        // Aggregated in memory: SQLite can't SUM decimals, and a personal ledger is small.
        var rows = await query
            .Select(t => new Row(t.Amount, t.Date, t.CategoryId,
                t.Category != null ? t.Category.Name : null,
                t.Category != null ? t.Category.Kind : null,
                t.Account.Currency))
            .ToListAsync();

        var currencies = rows.Select(r => r.Currency).Distinct().Order().ToList();
        if (currencies.Count > 1)
        {
            ModelState.AddModelError(nameof(currency),
                $"Transactions span several currencies ({string.Join(", ", currencies)}). Pass ?currency= to pick one.");
            return ValidationProblem(ModelState);
        }

        var income = rows.Where(r => r.Amount > 0).Sum(r => r.Amount);
        var expense = -rows.Where(r => r.Amount < 0).Sum(r => r.Amount);

        var byCategory = rows
            .GroupBy(r => (r.CategoryId, r.CategoryName, r.CategoryKind))
            .Select(g => new CategorySummaryDto(g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryKind,
                g.Sum(r => r.Amount), g.Count()))
            .OrderByDescending(c => Math.Abs(c.Total))
            .ToList();

        var byMonth = rows
            .GroupBy(r => r.Date.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var monthIncome = g.Where(r => r.Amount > 0).Sum(r => r.Amount);
                var monthExpense = -g.Where(r => r.Amount < 0).Sum(r => r.Amount);
                return new MonthSummaryDto(g.Key, monthIncome, monthExpense, monthIncome - monthExpense);
            })
            .ToList();

        return new SummaryDto(from, to, currency ?? currencies.SingleOrDefault(), rows.Count,
            income, expense, income - expense, byCategory, byMonth);
    }
}
