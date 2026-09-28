using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mavrylo.Filters;

/// <summary>
/// Hides legacy account-sync endpoints when the no-login device flow is the only supported mode.
/// Matches legacy login/register behavior by returning 404 instead of advertising the route.
/// </summary>
public sealed class LegacyAuthGuardFilter(IConfiguration config, IWebHostEnvironment env) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var enabled = config.GetValue("LegacyAuth:Enabled", env.IsDevelopment());
        if (!enabled)
            context.Result = new NotFoundResult();
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
