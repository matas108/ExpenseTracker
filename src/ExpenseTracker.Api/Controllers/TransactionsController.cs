using ExpenseTracker.Api.Auth;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using ExpenseTracker.Api.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/transactions")]
public class TransactionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetAll(
        int? accountId, int? categoryId, DateOnly? dateFrom, DateOnly? dateTo,
        int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var userId = User.GetUserId();
        var query = db.Transactions.AsNoTracking().Where(t => t.UserId == userId);
        if (accountId is not null) query = query.Where(t => t.AccountId == accountId);
        if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
        if (dateFrom is not null) query = query.Where(t => t.Date >= dateFrom);
        if (dateTo is not null) query = query.Where(t => t.Date <= dateTo);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return new PagedResult<TransactionDto>(items.Select(TransactionDto.From).ToList(), page, pageSize, total);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Get(int id)
    {
        var transaction = await Find(id);
        return transaction is null ? NotFound() : TransactionDto.From(transaction);
    }

    [HttpPost]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TransactionDto>> Create(TransactionRequest request)
    {
        if (!await ReferencesAreValid(request)) return ValidationProblem(ModelState);

        var transaction = new Transaction { UserId = User.GetUserId(), Source = TransactionSource.Manual };
        Apply(transaction, request);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = transaction.Id }, TransactionDto.From(transaction));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Update(int id, TransactionRequest request)
    {
        var transaction = await Find(id);
        if (transaction is null) return NotFound();
        if (!await ReferencesAreValid(request)) return ValidationProblem(ModelState);

        Apply(transaction, request);
        await db.SaveChangesAsync();
        return TransactionDto.From(transaction);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var transaction = await Find(id);
        if (transaction is null) return NotFound();

        db.Transactions.Remove(transaction);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportReport>> Import(
        [FromForm] ImportTransactionsForm form, [FromServices] CsvTransactionImporter importer, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (form.AccountId is not null && !await db.Accounts.AnyAsync(a => a.Id == form.AccountId && a.UserId == userId, ct))
        {
            ModelState.AddModelError(nameof(form.AccountId), "Account not found.");
            return ValidationProblem(ModelState);
        }

        try
        {
            await using var stream = form.File.OpenReadStream();
            return await importer.ImportAsync(userId, stream, form.AccountId, ct);
        }
        catch (ImportFileException ex)
        {
            ModelState.AddModelError(nameof(form.File), ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    private Task<Transaction?> Find(int id)
    {
        var userId = User.GetUserId();
        return db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    }

    // The account and category must exist and belong to the current user.
    private async Task<bool> ReferencesAreValid(TransactionRequest request)
    {
        var userId = User.GetUserId();

        if (!await db.Accounts.AnyAsync(a => a.Id == request.AccountId && a.UserId == userId))
            ModelState.AddModelError(nameof(request.AccountId), "Account not found.");

        if (request.CategoryId is not null
            && !await db.Categories.AnyAsync(c => c.Id == request.CategoryId && c.UserId == userId))
            ModelState.AddModelError(nameof(request.CategoryId), "Category not found.");

        return ModelState.IsValid;
    }

    private static void Apply(Transaction transaction, TransactionRequest request)
    {
        transaction.AccountId = request.AccountId!.Value;
        transaction.CategoryId = request.CategoryId;
        transaction.Amount = request.Amount!.Value;
        transaction.Date = request.Date!.Value;
        transaction.Description = request.Description?.Trim() ?? "";
    }
}
