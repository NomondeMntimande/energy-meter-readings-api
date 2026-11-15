using EnergyMeter.Api.Data;
using EnergyMeter.Core.Consumption;
using EnergyMeter.Core.Entities;
using EnergyMeter.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Services;

public enum CreateReadingStatus
{
    Created,
    MeterNotFound,
    DuplicateTimestamp
}

public sealed record CreateReadingResult(CreateReadingStatus Status, Reading? Reading = null);

public class ReadingService(EnergyDbContext db)
{
    public async Task<CreateReadingResult> CreateAsync(int meterId, DateTime timestamp, decimal valueKwh, CancellationToken ct = default)
    {
        if (!await db.Meters.AnyAsync(m => m.Id == meterId, ct))
        {
            return new CreateReadingResult(CreateReadingStatus.MeterNotFound);
        }

        var timestampUtc = UtcTime.Normalize(timestamp);

        // Fast path: give a clean 409 without relying on an exception.
        if (await ExistsAsync(meterId, timestampUtc, ct))
        {
            return new CreateReadingResult(CreateReadingStatus.DuplicateTimestamp);
        }

        var reading = new Reading { MeterId = meterId, TimestampUtc = timestampUtc, ValueKwh = valueKwh };
        db.Readings.Add(reading);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent requests can both pass the check above; the unique index is the
            // real guard. Re-check rather than parsing provider-specific error codes.
            db.Entry(reading).State = EntityState.Detached;
            if (await ExistsAsync(meterId, timestampUtc, ct))
            {
                return new CreateReadingResult(CreateReadingStatus.DuplicateTimestamp);
            }

            throw;
        }

        return new CreateReadingResult(CreateReadingStatus.Created, reading);
    }

    public Task<List<Reading>> ListAsync(int meterId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var query = db.Readings.AsNoTracking().Where(r => r.MeterId == meterId);

        if (fromUtc is { } from)
        {
            var f = UtcTime.Normalize(from);
            query = query.Where(r => r.TimestampUtc >= f);
        }

        if (toUtc is { } to)
        {
            var t = UtcTime.Normalize(to);
            query = query.Where(r => r.TimestampUtc <= t);
        }

        return query.OrderBy(r => r.TimestampUtc).ToListAsync(ct);
    }

    public async Task<ConsumptionSummary> GetSummaryAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var from = UtcTime.Normalize(fromUtc);
        var to = UtcTime.Normalize(toUtc);

        // Only pull the in-range points from the database; the calculation itself stays pure.
        var points = await db.Readings.AsNoTracking()
            .Where(r => r.MeterId == meterId && r.TimestampUtc >= from && r.TimestampUtc <= to)
            .OrderBy(r => r.TimestampUtc)
            .Select(r => new ReadingPoint(r.TimestampUtc, r.ValueKwh))
            .ToListAsync(ct);

        return ConsumptionCalculator.Calculate(points, from, to);
    }

    private Task<bool> ExistsAsync(int meterId, DateTime timestampUtc, CancellationToken ct) =>
        db.Readings.AnyAsync(r => r.MeterId == meterId && r.TimestampUtc == timestampUtc, ct);
}
