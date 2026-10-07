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

        // The database groups and sums; only one row per currency, category or month comes back.
        var currencies = await query
            .Select(t => t.Account.Currency)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
        if (currencies.Count > 1)
        {
            ModelState.AddModelError(nameof(currency),
                $"Transactions span several currencies ({string.Join(", ", currencies)}). Pass ?currency= to pick one.");
            return ValidationProblem(ModelState);
        }

        var byCategory = (await query
            .GroupBy(t => new
            {
                t.CategoryId,
                Name = t.Category != null ? t.Category.Name : null,
                Kind = t.Category != null ? (CategoryKind?)t.Category.Kind : null,
            })
            .Select(g => new CategorySummaryDto(g.Key.CategoryId, g.Key.Name, g.Key.Kind,
                g.Sum(t => t.Amount), g.Count()))
            .ToListAsync())
            .OrderByDescending(c => Math.Abs(c.Total))
            .ToList();

        var byMonth = (await query
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Sum(t => t.Amount > 0 ? t.Amount : 0m),
                Spent = g.Sum(t => t.Amount < 0 ? t.Amount : 0m), // negative
            })
            .ToListAsync())
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .Select(m => new MonthSummaryDto($"{m.Year:D4}-{m.Month:D2}", m.Income, -m.Spent, m.Income + m.Spent))
            .ToList();

        var income = byMonth.Sum(m => m.Income);
        var expense = byMonth.Sum(m => m.Expense);
        var transactionCount = byCategory.Sum(c => c.Count);

        return new SummaryDto(from, to, currency ?? currencies.SingleOrDefault(), transactionCount,
            income, expense, income - expense, byCategory, byMonth);

    }
}