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
                ["email"] = ["Informe um e-mail válido com até 254 caracteres."]
            });
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Informe uma senha."]
            });
        }

        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.ToArray();
            var field = errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName")
                ? "email" : "password";
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [field] = errors.Select(error => RegistrationErrorMessage(error.Code,
                    users.Options.Password.RequiredLength)).ToArray()
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
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Informe e-mail e senha.");
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha inválidos.");
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha inválidos.");
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

    private static string RegistrationErrorMessage(string code, int minimumLength) => code switch
    {
        "PasswordTooShort" => $"A senha deve ter pelo menos {minimumLength} caracteres.",
        "PasswordRequiresDigit" => "A senha deve conter pelo menos um número.",
        "PasswordRequiresLower" => "A senha deve conter pelo menos uma letra minúscula.",
        "PasswordRequiresUpper" => "A senha deve conter pelo menos uma letra maiúscula.",
        "PasswordRequiresNonAlphanumeric" => "A senha deve conter pelo menos um símbolo, como ! ou #.",
        "PasswordRequiresUniqueChars" => "A senha deve conter mais caracteres diferentes.",
        "DuplicateEmail" or "DuplicateUserName" => "Este e-mail já está cadastrado.",
        "InvalidEmail" or "InvalidUserName" => "Informe um e-mail válido.",
        _ => "Não foi possível criar a conta. Verifique os dados e tente novamente."
    };

    public sealed record RegisterRequest(string? Email, string? Password);
    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record UserResponse(string Id, string Email);
}
