using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using WebChangeRadar.Api.Data;

namespace WebChangeRadar.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = antiforgery.GetAndStoreTokens(context).RequestToken;
            return Results.Ok(new { requestToken = token });
        });

        auth.MapPost("/register", RegisterAsync)
            .RequireRateLimiting("auth")
            .AddEndpointFilter<RequireCsrfTokenFilter>();
        auth.MapPost("/login", LoginAsync)
            .RequireRateLimiting("auth")
            .AddEndpointFilter<RequireCsrfTokenFilter>();
        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter<RequireCsrfTokenFilter>();
        auth.MapGet("/me", MeAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Enter a valid email address of at most 254 characters."]
            });
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password is required."]
            });
        }

        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.ToArray();
            var field = errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")
                ? "email" : "password";
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [field] = errors.Select(error => error.Description).ToArray()
            });
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Created("/api/auth/me", new UserResponse(user.Id, user.Email!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Email and password are required.");
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");
        }

        return Results.Ok(new UserResponse(user.Id, user.Email!));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(context.User);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(new UserResponse(user.Id, user.Email!));
    }

    public sealed record RegisterRequest(string? Email, string? Password);
    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record UserResponse(string Id, string Email);
}
