using Microsoft.AspNetCore.Antiforgery;

namespace WebChangeRadar.Api.Features.Auth;

public sealed class RequireCsrfTokenFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid CSRF token.");
        }

        return await next(context);
    }
}
