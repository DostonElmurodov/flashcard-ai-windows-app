using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mavrylo.Tests.TestSupport;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private readonly string? external = Environment.GetEnvironmentVariable("OWL_TEST_POSTGRES");
    private readonly string database = "owl_test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(external))
        {
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("owl_ai_tests").WithUsername("owl_ai").WithPassword("owl_ai").Build();
            await container.StartAsync();
            ConnectionString = container.GetConnectionString();
            return;
        }
        // An explicitly supplied test server gets a fresh database per fixture, never its existing data.
        await using var connection = new NpgsqlConnection(external);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
        await command.ExecuteNonQueryAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = database }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (container != null) { await container.DisposeAsync(); return; }
        if (string.IsNullOrWhiteSpace(external)) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(external);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
