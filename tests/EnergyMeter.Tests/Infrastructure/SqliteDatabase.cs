using EnergyMeter.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Tests.Infrastructure;

/// <summary>
/// An in-memory SQLite database. Unlike the EF in-memory provider it is relational,
/// so the unique index on (MeterId, TimestampUtc) is actually enforced.
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<EnergyDbContext> Options =>
        new DbContextOptionsBuilder<EnergyDbContext>().UseSqlite(_connection).Options;

    public EnergyDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
