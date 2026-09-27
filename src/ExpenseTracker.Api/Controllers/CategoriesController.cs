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
[Route("api/categories")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CategoryDto>> GetAll()
    {
        var userId = User.GetUserId();
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .ToListAsync();
        return categories.Select(CategoryDto.From).ToList();
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> Get(int id)
    {
        var category = await Find(id);
        return category is null ? NotFound() : CategoryDto.From(category);
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request)
    {
        var category = new Category { UserId = User.GetUserId() };
        Apply(category, request);
        if (await NameTaken(category)) return NameConflict(category.Name);

        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = category.Id }, CategoryDto.From(category));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Update(int id, CategoryRequest request)
    {
        var category = await Find(id);
        if (category is null) return NotFound();

        Apply(category, request);
        if (await NameTaken(category)) return NameConflict(category.Name);

        await db.SaveChangesAsync();
        return CategoryDto.From(category);
    }

    // Transactions in a deleted category become uncategorised (FK is ON DELETE SET NULL).
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await Find(id);
        if (category is null) return NotFound();

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<Category?> Find(int id)
    {
        var userId = User.GetUserId();
        return db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
    }

    // Names are unique per user (a unique index backs this up).
    private Task<bool> NameTaken(Category category) =>
        db.Categories.AnyAsync(c =>
            c.UserId == category.UserId && c.Name == category.Name && c.Id != category.Id);

    private ObjectResult NameConflict(string name) =>
        Problem($"A category named '{name}' already exists.", statusCode: StatusCodes.Status409Conflict);

    private static void Apply(Category category, CategoryRequest request)
    {
        category.Name = request.Name.Trim();
        category.Kind = request.Kind!.Value;
    }
}
