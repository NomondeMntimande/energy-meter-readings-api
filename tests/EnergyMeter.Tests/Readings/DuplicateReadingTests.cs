using EnergyMeter.Api.Services;
using EnergyMeter.Core.Entities;
using EnergyMeter.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Tests.Readings;

public class DuplicateReadingTests : IDisposable
{
    private static readonly DateTime Timestamp = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteDatabase _database = new();
    private readonly int _meterId;
    private readonly int _otherMeterId;

    public DuplicateReadingTests()
    {
        using var db = _database.CreateContext();
        var site = new Site
        {
            Name = "Test site",
            Meters = [new Meter { SerialNumber = "A" }, new Meter { SerialNumber = "B" }]
        };
        db.Sites.Add(site);
        db.SaveChanges();
        _meterId = site.Meters[0].Id;
        _otherMeterId = site.Meters[1].Id;
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Second_reading_with_same_timestamp_for_same_meter_is_rejected()
    {
        await using var db = _database.CreateContext();
        var service = new ReadingService(db);

        var first = await service.CreateAsync(_meterId, Timestamp, 100m);
        var second = await service.CreateAsync(_meterId, Timestamp, 105m);

        Assert.Equal(CreateReadingStatus.Created, first.Status);
        Assert.Equal(CreateReadingStatus.DuplicateTimestamp, second.Status);
        Assert.Null(second.Reading);
        Assert.Equal(1, await db.Readings.CountAsync(r => r.MeterId == _meterId));
    }

    [Fact]
    public async Task Same_instant_expressed_with_an_offset_is_treated_as_a_duplicate()
    {
        await using var db = _database.CreateContext();
        var service = new ReadingService(db);
        await service.CreateAsync(_meterId, Timestamp, 100m);

        // 10:00 at +02:00 is 08:00Z. After JSON parsing it arrives as a Local-kind DateTime.
        var sameInstant = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(2)).LocalDateTime;

        var result = await service.CreateAsync(_meterId, sameInstant, 101m);

        Assert.Equal(CreateReadingStatus.DuplicateTimestamp, result.Status);
    }

    [Fact]
    public async Task Same_timestamp_on_a_different_meter_is_allowed()
    {
        await using var db = _database.CreateContext();
        var service = new ReadingService(db);

        var a = await service.CreateAsync(_meterId, Timestamp, 100m);
        var b = await service.CreateAsync(_otherMeterId, Timestamp, 200m);

        Assert.Equal(CreateReadingStatus.Created, a.Status);
        Assert.Equal(CreateReadingStatus.Created, b.Status);
    }

    [Fact]
    public async Task Database_unique_index_rejects_duplicates_that_bypass_the_service()
    {
        // The service check is a convenience; the index is the guarantee (e.g. concurrent inserts).
        await using (var db = _database.CreateContext())
        {
            db.Readings.Add(new Reading { MeterId = _meterId, TimestampUtc = Timestamp, ValueKwh = 100m });
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            db.Readings.Add(new Reading { MeterId = _meterId, TimestampUtc = Timestamp, ValueKwh = 101m });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Duplicate_that_slips_past_the_precheck_is_still_reported_as_duplicate()
    {
        // Simulate a race: a reading is inserted by "another request" after this service's
        // context has been created, and the service's insert then hits the unique index.
        await using var db = _database.CreateContext();
        db.SavingChanges += (_, _) =>
        {
            using var other = _database.CreateContext();
            if (!other.Readings.Any())
            {
                other.Readings.Add(new Reading { MeterId = _meterId, TimestampUtc = Timestamp, ValueKwh = 100m });
                other.SaveChanges();
            }
        };

        var result = await new ReadingService(db).CreateAsync(_meterId, Timestamp, 101m);

        Assert.Equal(CreateReadingStatus.DuplicateTimestamp, result.Status);
    }

    [Fact]
    public async Task Reading_for_unknown_meter_is_reported_as_not_found()
    {
        await using var db = _database.CreateContext();

        var result = await new ReadingService(db).CreateAsync(999, Timestamp, 1m);

        Assert.Equal(CreateReadingStatus.MeterNotFound, result.Status);
    }
}
