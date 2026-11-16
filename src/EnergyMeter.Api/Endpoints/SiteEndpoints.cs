using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Core.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Endpoints;

public static class SiteEndpoints
{
    public const int MaxNameLength = 200;

    public static RouteGroupBuilder MapSiteEndpoints(this RouteGroupBuilder api)
    {
        var sites = api.MapGroup("/sites").WithTags("Sites");

        sites.MapPost("/", CreateSite).WithName(nameof(CreateSite));
        sites.MapGet("/", ListSites).WithName(nameof(ListSites));
        sites.MapGet("/{siteId:int}", GetSite).WithName(nameof(GetSite))
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    private static async Task<Results<Created<SiteResponse>, ValidationProblem>> CreateSite(
        CreateSiteRequest request, EnergyDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ApiProblems.Validation("name", "Name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            return ApiProblems.Validation("name", $"Name must be {MaxNameLength} characters or fewer.");
        }

        var site = new Site { Name = name, CreatedUtc = clock.GetUtcNow().UtcDateTime };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/sites/{site.Id}", SiteResponse.From(site));
    }

    private static async Task<Ok<List<SiteResponse>>> ListSites(EnergyDbContext db, CancellationToken ct)
    {
        var sites = await db.Sites.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SiteResponse(s.Id, s.Name, s.CreatedUtc))
            .ToListAsync(ct);

        return TypedResults.Ok(sites);
    }

    private static async Task<Results<Ok<SiteResponse>, ProblemHttpResult>> GetSite(
        int siteId, EnergyDbContext db, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId, ct);
        return site is null ? ApiProblems.NotFound("Site", siteId) : TypedResults.Ok(SiteResponse.From(site));
    }
}
