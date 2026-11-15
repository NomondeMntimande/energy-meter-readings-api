using EnergyMeter.Core.Consumption;

namespace EnergyMeter.Tests.Consumption;

public class ConsumptionCalculatorTests
{
    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static ReadingPoint R(DateTime ts, decimal kwh) => new(ts, kwh);

    [Fact]
    public void Multi_day_readings_produce_total_and_daily_breakdown_attributed_to_later_reading()
    {
        var readings = new[]
        {
            R(Utc(1, 0), 100.0m),
            R(Utc(1, 12), 110.5m),  // +10.5 -> 1 Nov
            R(Utc(2, 1), 125.0m),   // +14.5 -> 2 Nov (pair spans midnight; goes to later reading's date)
            R(Utc(2, 23), 130.0m),  // +5.0  -> 2 Nov
            R(Utc(3, 6), 142.2m)    // +12.2 -> 3 Nov
        };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(3, 23));

        Assert.Equal(5, result.ReadingCount);
        Assert.Equal(42.2m, result.TotalConsumptionKwh);
        Assert.Equal(
            [
                new DailyConsumption(new DateOnly(2026, 11, 1), 10.5m),
                new DailyConsumption(new DateOnly(2026, 11, 2), 19.5m),
                new DailyConsumption(new DateOnly(2026, 11, 3), 12.2m)
            ],
            result.Daily);
        Assert.Equal(result.TotalConsumptionKwh, result.Daily.Sum(d => d.ConsumptionKwh));
        Assert.False(result.HasDecreasingReadings);
    }

    [Fact]
    public void Unordered_input_is_sorted_by_timestamp_before_calculating()
    {
        var readings = new[]
        {
            R(Utc(2, 8), 130m),
            R(Utc(1, 8), 100m),
            R(Utc(1, 20), 112m)
        };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(2, 23));

        Assert.Equal(30m, result.TotalConsumptionKwh);
        Assert.Equal(
            [new DailyConsumption(new DateOnly(2026, 11, 1), 12m), new DailyConsumption(new DateOnly(2026, 11, 2), 18m)],
            result.Daily);
    }

    [Fact]
    public void Range_is_inclusive_at_both_ends_and_excludes_readings_outside_it()
    {
        var readings = new[]
        {
            R(Utc(1, 7, 59), 90m),   // just before range
            R(Utc(1, 8), 100m),      // == fromUtc
            R(Utc(1, 12), 104m),
            R(Utc(1, 18), 109m),     // == toUtc
            R(Utc(1, 18, 1), 150m)   // just after range
        };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 8), Utc(1, 18));

        Assert.Equal(3, result.ReadingCount);
        Assert.Equal(9m, result.TotalConsumptionKwh);
        Assert.Equal([new DailyConsumption(new DateOnly(2026, 11, 1), 9m)], result.Daily);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_readings_in_range_gives_zero(int count)
    {
        var readings = Enumerable.Range(0, count).Select(i => R(Utc(1, 8 + i), 100m + i));

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(1, 23));

        Assert.Equal(count, result.ReadingCount);
        Assert.Equal(0m, result.TotalConsumptionKwh);
        Assert.Empty(result.Daily);
    }

    [Fact]
    public void Single_point_range_with_one_reading_gives_zero()
    {
        var at = Utc(1, 8);

        var result = ConsumptionCalculator.Calculate([R(at, 100m)], at, at);

        Assert.Equal(1, result.ReadingCount);
        Assert.Equal(0m, result.TotalConsumptionKwh);
    }

    [Fact]
    public void Days_without_a_reading_pair_are_omitted()
    {
        var readings = new[]
        {
            R(Utc(1, 8), 100m),
            R(Utc(4, 8), 160m) // gap of several days; whole delta lands on 4 Nov
        };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(5, 0));

        Assert.Equal([new DailyConsumption(new DateOnly(2026, 11, 4), 60m)], result.Daily);
    }

    [Fact]
    public void Decreasing_reading_is_excluded_from_daily_totals_and_flagged()
    {
        var readings = new[]
        {
            R(Utc(1, 8), 9990m),
            R(Utc(1, 20), 9998m),  // +8 -> 1 Nov
            R(Utc(2, 2), 3m),      // meter reset: negative, ignored in daily
            R(Utc(2, 10), 15m)     // +12 -> 2 Nov
        };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(2, 23));

        // Total follows the stated rule literally (latest - earliest).
        Assert.Equal(15m - 9990m, result.TotalConsumptionKwh);
        Assert.Equal(
            [new DailyConsumption(new DateOnly(2026, 11, 1), 8m), new DailyConsumption(new DateOnly(2026, 11, 2), 12m)],
            result.Daily);
        Assert.True(result.HasDecreasingReadings);
    }

    [Fact]
    public void Flat_readings_give_zero_consumption_but_keep_the_day()
    {
        var readings = new[] { R(Utc(1, 8), 100m), R(Utc(1, 9), 100m) };

        var result = ConsumptionCalculator.Calculate(readings, Utc(1, 0), Utc(1, 23));

        Assert.Equal(0m, result.TotalConsumptionKwh);
        Assert.Equal([new DailyConsumption(new DateOnly(2026, 11, 1), 0m)], result.Daily);
    }

    [Fact]
    public void Inverted_range_throws()
    {
        Assert.Throws<ArgumentException>(() => ConsumptionCalculator.Calculate([], Utc(2, 0), Utc(1, 0)));
    }
}
