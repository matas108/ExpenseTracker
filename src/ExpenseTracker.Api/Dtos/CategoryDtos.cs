using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Api.Entities;

namespace ExpenseTracker.Api.Dtos;

public record CategoryDto(int Id, string Name, CategoryKind Kind, DateTime CreatedAt)
{
    public static CategoryDto From(Category c) => new(c.Id, c.Name, c.Kind, c.CreatedAt);
}

// Used for both POST and PUT.
public record CategoryRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] CategoryKind? Kind);
