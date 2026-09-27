using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.Dtos;

public record RegisterRequest([Required, EmailAddress] string Email, [Required] string Password);

public record LoginRequest([Required] string Email, [Required] string Password);

public record AuthResponse(string Token, DateTime ExpiresAt, string UserId, string Email);
