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
[Route("api/accounts")]
public class AccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AccountDto>> GetAll()
    {
        var userId = User.GetUserId();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Name)
            .ToListAsync();
        return accounts.Select(AccountDto.From).ToList();
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Get(int id)
    {
        var account = await Find(id);
        return account is null ? NotFound() : AccountDto.From(account);
    }

    [HttpGet("{id:int}/balance")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountBalanceDto>> GetBalance(int id)
    {
        var account = await Find(id);
        if (account is null) return NotFound();

        var transactionsTotal = await db.Transactions
            .Where(t => t.AccountId == id)
            .SumAsync(t => t.Amount);

        return new AccountBalanceDto(account.Id, account.Currency, account.OpeningBalance,
            transactionsTotal, account.OpeningBalance + transactionsTotal);
    }

    [HttpPost]
    [ProducesResponseType<AccountDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AccountDto>> Create(AccountRequest request)
    {
        var account = new Account { UserId = User.GetUserId() };
        Apply(account, request);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = account.Id }, AccountDto.From(account));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Update(int id, AccountRequest request)
    {
        var account = await Find(id);
        if (account is null) return NotFound();

        Apply(account, request);
        await db.SaveChangesAsync();
        return AccountDto.From(account);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await Find(id);
        if (account is null) return NotFound();

        if (await db.Transactions.AnyAsync(t => t.AccountId == id))
            return Problem("Account still has transactions. Delete or move them first.",
                statusCode: StatusCodes.Status409Conflict);

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<Account?> Find(int id)
    {
        var userId = User.GetUserId();
        return db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
    }

    private static void Apply(Account account, AccountRequest request)
    {
        account.Name = request.Name.Trim();
        account.Type = request.Type!.Value;
        account.Currency = request.Currency.ToUpperInvariant();
        account.OpeningBalance = request.OpeningBalance;
    }
}
