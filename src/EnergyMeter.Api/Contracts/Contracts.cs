using EnergyMeter.Core.Consumption;
using EnergyMeter.Core.Entities;

namespace EnergyMeter.Api.Contracts;

// Requests. Properties are nullable so a missing field becomes a validation error, not a default value.

public sealed record CreateSiteRequest(string? Name);

public sealed record CreateMeterRequest(string? SerialNumber);

public sealed record CreateReadingRequest(DateTime? TimestampUtc, decimal? ValueKwh);

// Responses. Entities are never returned directly, so the persistence model can change freely.

public sealed record SiteResponse(int Id, string Name, DateTime CreatedUtc)
{
    public static SiteResponse From(Site s) => new(s.Id, s.Name, s.CreatedUtc);
}

public sealed record MeterResponse(int Id, int SiteId, string SerialNumber, DateTime CreatedUtc)
{
    public static MeterResponse From(Meter m) => new(m.Id, m.SiteId, m.SerialNumber, m.CreatedUtc);
}

public sealed record ReadingResponse(long Id, int MeterId, DateTime TimestampUtc, decimal ValueKwh)
{
    public static ReadingResponse From(Reading r) => new(r.Id, r.MeterId, r.TimestampUtc, r.ValueKwh);
}

public sealed record DailyConsumptionResponse(DateOnly Date, decimal ConsumptionKwh);

public sealed record ConsumptionSummaryResponse(
    int MeterId,
    DateTime FromUtc,
    DateTime ToUtc,
    int ReadingCount,
    decimal TotalConsumptionKwh,
    IReadOnlyList<DailyConsumptionResponse> Daily,
    bool HasDecreasingReadings)
{
    public static ConsumptionSummaryResponse From(int meterId, DateTime fromUtc, DateTime toUtc, ConsumptionSummary s) =>
        new(meterId, fromUtc, toUtc, s.ReadingCount, s.TotalConsumptionKwh,
            s.Daily.Select(d => new DailyConsumptionResponse(d.Date, d.ConsumptionKwh)).ToList(),
            s.HasDecreasingReadings);
}
