using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Entities;

// Id, Email and PasswordHash come from IdentityUser.
public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
