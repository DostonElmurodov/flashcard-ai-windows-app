using Microsoft.AspNetCore.Mvc;
using Xunit;
using Mavrylo.Services;
using Microsoft.Extensions.Configuration;
namespace Mavrylo.Tests;
public class AccountContractTests
{
    [Theory]
    [InlineData("legacy", "legacy/")]
    [InlineData("legacy/", "legacy")]
    [InlineData("legacy", "device/")]
    [InlineData("legacy", "device")]
    public void AccountAudienceRejectsSlashEquivalentLegacyAndDeviceAudiences(string legacy, string account)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Audience"] = legacy,
            ["Account:Audience"] = account
        }).Build();
        Assert.Throws<InvalidOperationException>(() => AccountAuth.Audience(config));
    }
    [Fact]
    public void DedicatedAccountRouteExists()
    {
        var controller = typeof(Program).Assembly.GetType("Mavrylo.Areas.OwlAi.Controllers.AccountController");
        Assert.NotNull(controller);
        Assert.Equal("owlai/account", controller.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template);
    }
}
