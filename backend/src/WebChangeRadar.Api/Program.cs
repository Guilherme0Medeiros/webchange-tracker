using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Threading.RateLimiting;
using WebChangeRadar.Api.Data;
using WebChangeRadar.Api.Configuration;
using WebChangeRadar.Api.Features.Auth;
using WebChangeRadar.Api.Features.Monitors;
using WebChangeRadar.Api.Services.Fetching;
using WebChangeRadar.Api.Services.Extraction;
using WebChangeRadar.Api.Features.Checks;
using WebChangeRadar.Api.Features.Notifications;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    foreach (var setting in LocalEnvFile.Read())
    {
        var key = setting.Key.Replace("__", ":", StringComparison.Ordinal);
        if (builder.Configuration[key] is null)
            builder.Configuration[key] = setting.Value;
    }
}

builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>()
        .GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "__Host-WebChangeRadar.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Entre na sua conta para continuar.").ExecuteAsync(context.HttpContext);
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Você não tem permissão para acessar este recurso.").ExecuteAsync(context.HttpContext);
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-WebChangeRadar.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
    var defaultTitle = ReasonPhrases.GetReasonPhrase(status);
    if (string.IsNullOrWhiteSpace(context.ProblemDetails.Title) || context.ProblemDetails.Title == defaultTitle)
        context.ProblemDetails.Title = status switch
        {
            400 => "Solicitação inválida.",
            401 => "Entre na sua conta para continuar.",
            403 => "Você não tem permissão para acessar este recurso.",
            404 => "Recurso não encontrado.",
            405 => "Operação não permitida para este endereço.",
            429 => "Muitas solicitações. Aguarde e tente novamente.",
            _ => "Não foi possível concluir a solicitação. Tente novamente mais tarde."
        };
    if (status >= 500) context.ProblemDetails.Detail = null;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
        new ValueTask(Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
            title: "Muitas solicitações. Aguarde e tente novamente.").ExecuteAsync(context.HttpContext));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddHealthChecks();
builder.Services.AddPageFetching();
builder.Services.AddSingleton<IContentExtractor, ContentExtractor>();
builder.Services.AddScoped<CheckProcessor>();
builder.Services.AddScoped<EmailOutboxProcessor>();
builder.Services.AddSingleton<IChangeEmailSender, SmtpChangeEmailSender>();
builder.Services.AddSingleton<CheckSchedule>();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var status = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error
        is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
    await Results.Problem(statusCode: status,
        title: status == StatusCodes.Status400BadRequest
            ? "Solicitação inválida. Verifique os dados enviados."
            : "Não foi possível concluir a solicitação. Tente novamente mais tarde.")
        .ExecuteAsync(context);
}));
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    if (!context.Request.Path.StartsWithSegments("/api")) return;
    await Results.Problem(statusCode: context.Response.StatusCode).ExecuteAsync(context);
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMonitorEndpoints();
app.MapCheckProcessingEndpoints();
app.MapCheckEndpoints();
app.MapNotificationEndpoints();

app.Run();

public partial class Program { }
