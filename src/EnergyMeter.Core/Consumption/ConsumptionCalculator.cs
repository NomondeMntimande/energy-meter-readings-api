namespace EnergyMeter.Core.Consumption;

public readonly record struct ReadingPoint(DateTime TimestampUtc, decimal ValueKwh);

public sealed record DailyConsumption(DateOnly Date, decimal ConsumptionKwh);

public sealed record ConsumptionSummary(
    int ReadingCount,
    decimal TotalConsumptionKwh,
    IReadOnlyList<DailyConsumption> Daily,
    bool HasDecreasingReadings);

/// <summary>
/// Pure calculation of consumption from cumulative readings. No I/O, so it is unit tested directly.
/// </summary>
public static class ConsumptionCalculator
{
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    /// <item>Only readings with fromUtc &lt;= timestamp &lt;= toUtc are used.</item>
    /// <item>Total = latest value - earliest value (0 if fewer than two readings).</item>
    /// <item>Daily = consecutive differences, attributed to the UTC date of the later reading,
    /// summing positive differences only.</item>
    /// </list>
    /// </remarks>
    public static ConsumptionSummary Calculate(IEnumerable<ReadingPoint> readings, DateTime fromUtc, DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (fromUtc > toUtc)
        {
            throw new ArgumentException("fromUtc must be on or before toUtc.", nameof(fromUtc));
        }

        var ordered = readings
            .Where(r => r.TimestampUtc >= fromUtc && r.TimestampUtc <= toUtc)
            .OrderBy(r => r.TimestampUtc)
            .ToList();

        if (ordered.Count < 2)
        {
            return new ConsumptionSummary(ordered.Count, 0m, [], false);
        }

        var total = ordered[^1].ValueKwh - ordered[0].ValueKwh;
        var daily = new SortedDictionary<DateOnly, decimal>();
        var hasDecrease = false;

        for (var i = 1; i < ordered.Count; i++)
        {
            var delta = ordered[i].ValueKwh - ordered[i - 1].ValueKwh;
            var day = DateOnly.FromDateTime(ordered[i].TimestampUtc);

            if (delta < 0)
            {
                // Likely a meter reset/rollover or bad data. Excluded from the daily figures
                // and flagged so the caller knows daily totals will not reconcile with the total.
                hasDecrease = true;
                continue;
            }

            daily[day] = daily.GetValueOrDefault(day) + delta;
        }

        var breakdown = daily.Select(kv => new DailyConsumption(kv.Key, kv.Value)).ToList();
        return new ConsumptionSummary(ordered.Count, total, breakdown, hasDecrease);
    }
}
