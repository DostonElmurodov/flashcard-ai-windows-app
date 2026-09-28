using Mavrylo.Dtos;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

/// <summary>
/// App Store Server Notifications v2 webhook. Apple POSTs a signed JWS envelope here when
/// subscription state changes (renew, expire, billing retry / grace, refund / revoke). Public and
/// unauthenticated — trust comes from JWS signature verification, not a bearer token. Needs a
/// publicly reachable HTTPS URL in production (cannot be exercised from localhost).
///
/// The signedPayload's <c>data</c> contains signedTransactionInfo + signedRenewalInfo (themselves
/// JWS). We decode the transaction, project it, and upsert so the entitlement reflects Apple's
/// latest truth even when the app is offline.
/// </summary>
[ApiController]
[Area("OwlAI")]
[Route("owlai/app-store-notifications")]
[AllowAnonymous]
public class AppStoreNotificationsController(AppStoreNotificationService notifications) : ControllerBase
{
    [HttpPost("notifications")]
    public async Task<IActionResult> Notifications([FromBody] AppStoreNotificationEnvelope envelope, CancellationToken ct)
    {
        var status = await notifications.HandleAsync(envelope, ct);
        return status switch
        {
            200 => Ok(),
            400 => BadRequest(),
            500 => StatusCode(500),
            _ => StatusCode(status)
        };
    }
}
