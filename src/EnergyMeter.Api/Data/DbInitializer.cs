using EnergyMeter.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Data;

public static class DbInitializer
{
    /// <summary>
    /// Applies migrations and seeds demo data when enabled in configuration (Development only by default).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        var applyMigrations = config.GetValue<bool>("Database:ApplyMigrationsOnStartup");
        var seed = config.GetValue<bool>("Database:SeedDemoData");
        if (!applyMigrations && !seed)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();

        if (applyMigrations)
        {
            await db.Database.MigrateAsync();
        }

        if (seed && !await db.Sites.AnyAsync())
        {
            db.Sites.Add(BuildDemoSite());
            await db.SaveChangesAsync();
        }
    }

    // One site, one meter, readings across two days. Enough to call the summary endpoint straight away.
    private static Site BuildDemoSite()
    {
        static DateTime Utc(int day, int hour) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

        return new Site
        {
            Name = "Demo Site - Cape Town Office",
            CreatedUtc = DateTime.UtcNow,
            Meters =
            [
                new Meter
                {
                    SerialNumber = "MTR-0001",
                    CreatedUtc = DateTime.UtcNow,
                    Readings =
                    [
                        new Reading { TimestampUtc = Utc(1, 0), ValueKwh = 12500.0m },
                        new Reading { TimestampUtc = Utc(1, 8), ValueKwh = 12512.5m },
                        new Reading { TimestampUtc = Utc(1, 18), ValueKwh = 12530.0m },
                        new Reading { TimestampUtc = Utc(2, 8), ValueKwh = 12543.7m },
                        new Reading { TimestampUtc = Utc(2, 18), ValueKwh = 12561.2m }
                    ]
                }
            ]
        };
    }
}
