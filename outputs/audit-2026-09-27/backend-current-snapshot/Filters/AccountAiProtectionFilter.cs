using Mavrylo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mavrylo.Filters;

public sealed class AccountAiProtectionFilter(AccountEntitlementService entitlements, AccountAiUsageService usage,
    IConfiguration configuration) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var account = http.User.FindFirst("sub")?.Value;
        if (account == null) { context.Result = new UnauthorizedResult(); return; }
        if (TestModePolicy.IsEnabled(configuration))
        {
            await next();
            return;
        }
        var entitlement = await entitlements.GetAsync(account, http.RequestAborted);
        if (!AccountEntitlementService.IsActive(entitlement.Status))
        {
            context.Result = new ObjectResult(new { code = "subscription_required", error = "An active shared subscription is required." }) { StatusCode = 402 };
            return;
        }
        if (!await usage.TryConsumeAsync(account, http.RequestAborted))
        {
            http.Response.Headers.RetryAfter = "60";
            context.Result = new ObjectResult(new { code = "account_ai_limit", error = "Account AI limit reached." }) { StatusCode = 429 };
            return;
        }
        await next();
    }
}
