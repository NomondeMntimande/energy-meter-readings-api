using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Core.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Endpoints;

public static class MeterEndpoints
{
    public const int MaxSerialNumberLength = 100;

    public static RouteGroupBuilder MapMeterEndpoints(this RouteGroupBuilder api)
    {
        // Meters are created and listed in the context of their site...
        var siteMeters = api.MapGroup("/sites/{siteId:int}/meters").WithTags("Meters");
        siteMeters.MapPost("/", CreateMeter).WithName(nameof(CreateMeter))
            .ProducesProblem(StatusCodes.Status404NotFound);
        siteMeters.MapGet("/", ListMetersForSite).WithName(nameof(ListMetersForSite))
            .ProducesProblem(StatusCodes.Status404NotFound);

        // ...but addressed directly once they exist, so reading URLs stay short.
        api.MapGet("/meters/{meterId:int}", GetMeter).WithTags("Meters").WithName(nameof(GetMeter))
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    private static async Task<Results<Created<MeterResponse>, ValidationProblem, ProblemHttpResult>> CreateMeter(
        int siteId, CreateMeterRequest request, EnergyDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var serial = request.SerialNumber?.Trim();
        if (string.IsNullOrEmpty(serial))
        {
            return ApiProblems.Validation("serialNumber", "Serial number is required.");
        }

        if (serial.Length > MaxSerialNumberLength)
        {
            return ApiProblems.Validation("serialNumber", $"Serial number must be {MaxSerialNumberLength} characters or fewer.");
        }

        if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct))
        {
            return ApiProblems.NotFound("Site", siteId);
        }

        var meter = new Meter { SiteId = siteId, SerialNumber = serial, CreatedUtc = clock.GetUtcNow().UtcDateTime };
        db.Meters.Add(meter);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/meters/{meter.Id}", MeterResponse.From(meter));
    }

    private static async Task<Results<Ok<List<MeterResponse>>, ProblemHttpResult>> ListMetersForSite(
        int siteId, EnergyDbContext db, CancellationToken ct)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct))
        {
            return ApiProblems.NotFound("Site", siteId);
        }

        var meters = await db.Meters.AsNoTracking()
            .Where(m => m.SiteId == siteId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterResponse(m.Id, m.SiteId, m.SerialNumber, m.CreatedUtc))
            .ToListAsync(ct);

        return TypedResults.Ok(meters);
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> GetMeter(
        int meterId, EnergyDbContext db, CancellationToken ct)
    {
        var meter = await db.Meters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meterId, ct);
        return meter is null ? ApiProblems.NotFound("Meter", meterId) : TypedResults.Ok(MeterResponse.From(meter));
    }
}
