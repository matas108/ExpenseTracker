using ExpenseTracker.Api.Auth;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    TokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new AppUser { UserName = request.Email, Email = request.Email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(user));
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return Unauthorized();

        // Counts failures toward Identity's lockout (5 attempts -> 5 minutes by default).
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();

        return Ok(ToResponse(user));
    }

    private AuthResponse ToResponse(AppUser user)
    {
        var (token, expiresAt) = tokens.CreateToken(user);
        return new AuthResponse(token, expiresAt, user.Id, user.Email!);
    }
}
