using Mavrylo.Filters;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Mavrylo.Tests;

public class LegacyAuthGuardFilterTests
{
    [Fact]
    public void Disabled_ShortCircuitsWith404()
    {
        var filter = new LegacyAuthGuardFilter(
            TestConfig.Create(new Dictionary<string, string?> { ["LegacyAuth:Enabled"] = "false" }),
            new FakeEnvironment("Production"));
        var context = CreateContext();

        filter.OnActionExecuting(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Fact]
    public void Enabled_PassesThrough()
    {
        var filter = new LegacyAuthGuardFilter(
            TestConfig.Create(new Dictionary<string, string?> { ["LegacyAuth:Enabled"] = "true" }),
            new FakeEnvironment("Production"));
        var context = CreateContext();

        filter.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Defaults_ToDevelopmentOnlyWhenUnset()
    {
        var config = TestConfig.Create();

        var dev = new LegacyAuthGuardFilter(config, new FakeEnvironment("Development"));
        var devContext = CreateContext();
        dev.OnActionExecuting(devContext);
        Assert.Null(devContext.Result);

        var prod = new LegacyAuthGuardFilter(config, new FakeEnvironment("Production"));
        var prodContext = CreateContext();
        prod.OnActionExecuting(prodContext);
        Assert.IsType<NotFoundResult>(prodContext.Result);
    }

    private static ActionExecutingContext CreateContext()
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }
}
