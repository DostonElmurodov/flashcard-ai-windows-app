using System.Security.Claims;
using Mavrylo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mavrylo.Filters;

public sealed class AccountClaimProofFilter(SharedAccountAuthentication authentication, AppAttestAssertionFilter assertion) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var device = authentication.ValidateBearer(http.Request.Headers["X-Device-Authorization"].ToString(), DeviceAuth.Audience);
        var keyId = device?.FindFirst("keyId")?.Value;
        if (string.IsNullOrWhiteSpace(keyId))
        {
            context.Result = new UnauthorizedObjectResult(new { code = "invalid_device", error = "A device token is required." });
            return;
        }
        var accountPrincipal = http.User;
        http.Items["ClaimDeviceKeyId"] = keyId;
        // Reuse exactly the existing raw-body, challenge/path/key/counter assertion verification.
        http.User = device!;
        try
        {
            await assertion.OnResourceExecutionAsync(context, async () =>
            {
                http.User = accountPrincipal;
                return await next();
            });
        }
        finally { http.User = accountPrincipal; }
    }
}
