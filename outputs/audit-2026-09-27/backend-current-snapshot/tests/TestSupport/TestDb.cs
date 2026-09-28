using Mavrylo.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Tests.TestSupport;

internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    private TestDb(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static TestDb Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return new TestDb(connection, db);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
