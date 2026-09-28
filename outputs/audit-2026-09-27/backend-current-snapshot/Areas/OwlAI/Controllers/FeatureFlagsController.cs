using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController, Route("owlai/config/feature-flags"), AllowAnonymous]
public sealed class FeatureFlagsController(IConfiguration configuration) : ControllerBase
{
    public sealed record FeatureFlags(bool TestMode);

    [HttpGet]
    public ActionResult<FeatureFlags> Get()
    {
        Response.Headers.CacheControl = "no-store";
        return new FeatureFlags(TestModePolicy.IsEnabled(configuration));
    }
}
