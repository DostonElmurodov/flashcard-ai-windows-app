using System.Net;
using System.Text.Json;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class PostgresIntegrationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task ApiStartsAgainstPostgresContainer_RunsMigrations_AndServesHealth()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }
}
