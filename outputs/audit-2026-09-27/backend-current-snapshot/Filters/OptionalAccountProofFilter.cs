using Microsoft.AspNetCore.Mvc.Filters;
namespace Mavrylo.Filters;

// A missing proof permits existing iOS login; a supplied proof must fully verify.
public sealed class OptionalAccountProofFilter(AccountClaimProofFilter proof) : IAsyncResourceFilter
{
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        => context.HttpContext.Request.Headers.ContainsKey("X-Device-Authorization")
            ? proof.OnResourceExecutionAsync(context, next) : Next(next);
    private static async Task Next(ResourceExecutionDelegate next) => await next();
}
