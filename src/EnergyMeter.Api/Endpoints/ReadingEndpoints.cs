using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Api.Services;
using EnergyMeter.Core.Time;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Endpoints;

public static class ReadingEndpoints
{
    public static RouteGroupBuilder MapReadingEndpoints(this RouteGroupBuilder api)
    {
        var meter = api.MapGroup("/meters/{meterId:int}");

        meter.MapPost("/readings", CreateReading).WithTags("Readings").WithName(nameof(CreateReading))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        meter.MapGet("/readings", ListReadings).WithTags("Readings").WithName(nameof(ListReadings))
            .ProducesProblem(StatusCodes.Status404NotFound);
        meter.MapGet("/readings/{readingId:long}", GetReading).WithTags("Readings").WithName(nameof(GetReading))
            .ProducesProblem(StatusCodes.Status404NotFound);
        meter.MapGet("/summary", GetSummary).WithTags("Summary").WithName(nameof(GetSummary))
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    private static async Task<Results<Created<ReadingResponse>, ValidationProblem, ProblemHttpResult>> CreateReading(
        int meterId, CreateReadingRequest request, ReadingService readings, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.TimestampUtc is null)
        {
            errors["timestampUtc"] = ["timestampUtc is required (ISO 8601 UTC, e.g. 2026-11-01T08:00:00Z)."];
        }

        if (request.ValueKwh is null)
        {
            errors["valueKwh"] = ["valueKwh is required."];
        }
        else if (request.ValueKwh < 0)
        {
            errors["valueKwh"] = ["valueKwh is a cumulative register value and cannot be negative."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var result = await readings.CreateAsync(meterId, request.TimestampUtc!.Value, request.ValueKwh!.Value, ct);

        return result.Status switch
        {
            CreateReadingStatus.Created => TypedResults.Created(
                $"/api/meters/{meterId}/readings/{result.Reading!.Id}", ReadingResponse.From(result.Reading)),
            CreateReadingStatus.MeterNotFound => ApiProblems.NotFound("Meter", meterId),
            CreateReadingStatus.DuplicateTimestamp => ApiProblems.Conflict(
                "Duplicate reading timestamp",
                $"Meter {meterId} already has a reading at {UtcTime.Normalize(request.TimestampUtc.Value):yyyy-MM-ddTHH:mm:ss.FFFFFFFZ}."),
            _ => throw new InvalidOperationException($"Unhandled status {result.Status}.")
        };
    }

    private static async Task<Results<Ok<List<ReadingResponse>>, ValidationProblem, ProblemHttpResult>> ListReadings(
        int meterId, DateTime? fromUtc, DateTime? toUtc, EnergyDbContext db, ReadingService readings, CancellationToken ct)
    {
        if (ApiProblems.InvalidRange(fromUtc, toUtc) is { } invalid)
        {
            return invalid;
        }

        if (!await db.Meters.AnyAsync(m => m.Id == meterId, ct))
        {
            return ApiProblems.NotFound("Meter", meterId);
        }

        var list = await readings.ListAsync(meterId, fromUtc, toUtc, ct);
        return TypedResults.Ok(list.Select(ReadingResponse.From).ToList());
    }

    private static async Task<Results<Ok<ReadingResponse>, ProblemHttpResult>> GetReading(
        int meterId, long readingId, EnergyDbContext db, CancellationToken ct)
    {
        var reading = await db.Readings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == readingId && r.MeterId == meterId, ct);

        return reading is null ? ApiProblems.NotFound("Reading", readingId) : TypedResults.Ok(ReadingResponse.From(reading));
    }

    private static async Task<Results<Ok<ConsumptionSummaryResponse>, ValidationProblem, ProblemHttpResult>> GetSummary(
        int meterId, DateTime? fromUtc, DateTime? toUtc, EnergyDbContext db, ReadingService readings, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (fromUtc is null) errors["fromUtc"] = ["fromUtc is required."];
        if (toUtc is null) errors["toUtc"] = ["toUtc is required."];
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (ApiProblems.InvalidRange(fromUtc, toUtc) is { } invalid)
        {
            return invalid;
        }

        if (!await db.Meters.AnyAsync(m => m.Id == meterId, ct))
        {
            return ApiProblems.NotFound("Meter", meterId);
        }

        var from = UtcTime.Normalize(fromUtc!.Value);
        var to = UtcTime.Normalize(toUtc!.Value);
        var summary = await readings.GetSummaryAsync(meterId, from, to, ct);

        return TypedResults.Ok(ConsumptionSummaryResponse.From(meterId, from, to, summary));
    }
}
