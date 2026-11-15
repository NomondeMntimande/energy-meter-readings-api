using System.Net;
using System.Net.Http.Json;
using EnergyMeter.Api.Contracts;
using EnergyMeter.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Tests.Api;

/// <summary>
/// Light end-to-end checks through HTTP: routing, status codes and JSON shapes.
/// The calculation and duplicate rules are covered in depth by the focused tests.
/// </summary>
public class ApiEndpointTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ApiEndpointTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Full_flow_create_site_meter_readings_then_summary()
    {
        var meter = await CreateMeterAsync();

        foreach (var (ts, kwh) in new[]
                 {
                     ("2026-11-01T00:00:00Z", 1000.0m),
                     ("2026-11-01T12:00:00Z", 1010.0m),
                     ("2026-11-02T06:00:00Z", 1025.5m)
                 })
        {
            var res = await _client.PostAsJsonAsync($"/api/meters/{meter.Id}/readings", new { timestampUtc = ts, valueKwh = kwh });
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            Assert.NotNull(res.Headers.Location);
        }

        var summary = await _client.GetFromJsonAsync<ConsumptionSummaryResponse>(
            $"/api/meters/{meter.Id}/summary?fromUtc=2026-11-01T00:00:00Z&toUtc=2026-11-02T23:59:59Z");

        Assert.NotNull(summary);
        Assert.Equal(3, summary.ReadingCount);
        Assert.Equal(25.5m, summary.TotalConsumptionKwh);
        Assert.Equal(
            [new DailyConsumptionResponse(new DateOnly(2026, 11, 1), 10.0m), new DailyConsumptionResponse(new DateOnly(2026, 11, 2), 15.5m)],
            summary.Daily);
    }

    [Fact]
    public async Task Duplicate_reading_timestamp_returns_409_problem_details()
    {
        var meter = await CreateMeterAsync();
        var body = new { timestampUtc = "2026-11-01T08:00:00Z", valueKwh = 12543.7m };

        var first = await _client.PostAsJsonAsync($"/api/meters/{meter.Id}/readings", body);
        var second = await _client.PostAsJsonAsync($"/api/meters/{meter.Id}/readings", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Duplicate reading timestamp", problem?.Title);
    }

    [Fact]
    public async Task Listing_readings_filters_by_inclusive_time_range()
    {
        var meter = await CreateMeterAsync();
        foreach (var hour in new[] { 6, 8, 10, 12 })
        {
            await _client.PostAsJsonAsync($"/api/meters/{meter.Id}/readings",
                new { timestampUtc = $"2026-11-01T{hour:00}:00:00Z", valueKwh = 100m + hour });
        }

        var readings = await _client.GetFromJsonAsync<List<ReadingResponse>>(
            $"/api/meters/{meter.Id}/readings?fromUtc=2026-11-01T08:00:00Z&toUtc=2026-11-01T10:00:00Z");

        Assert.NotNull(readings);
        Assert.Equal([8, 10], readings.Select(r => r.TimestampUtc.Hour));
        Assert.All(readings, r => Assert.Equal(DateTimeKind.Utc, r.TimestampUtc.Kind));
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_field_errors()
    {
        var meter = await CreateMeterAsync();

        var res = await _client.PostAsJsonAsync($"/api/meters/{meter.Id}/readings", new { valueKwh = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("timestampUtc", problem.Errors.Keys);
        Assert.Contains("valueKwh", problem.Errors.Keys);
    }

    [Fact]
    public async Task Summary_requires_a_valid_range()
    {
        var meter = await CreateMeterAsync();

        var missing = await _client.GetAsync($"/api/meters/{meter.Id}/summary?fromUtc=2026-11-01T00:00:00Z");
        var inverted = await _client.GetAsync($"/api/meters/{meter.Id}/summary?fromUtc=2026-11-02T00:00:00Z&toUtc=2026-11-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    [Fact]
    public async Task Malformed_input_returns_400_not_500()
    {
        var meter = await CreateMeterAsync();

        var badQuery = await _client.GetAsync($"/api/meters/{meter.Id}/summary?fromUtc=not-a-date&toUtc=2026-11-01T00:00:00Z");
        var badJson = await _client.PostAsync($"/api/meters/{meter.Id}/readings",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, badQuery.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);
        Assert.Equal("application/problem+json", badQuery.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_parent_resources_return_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/sites/99999/meters")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/meters/99999/readings", new { timestampUtc = "2026-11-01T08:00:00Z", valueKwh = 1m })).StatusCode);
    }

    [Fact]
    public async Task Demo_page_is_served_at_the_root()
    {
        var res = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
    }

    private async Task<MeterResponse> CreateMeterAsync()
    {
        var siteRes = await _client.PostAsJsonAsync("/api/sites", new { name = "Site " + Guid.NewGuid() });
        siteRes.EnsureSuccessStatusCode();
        var site = (await siteRes.Content.ReadFromJsonAsync<SiteResponse>())!;

        var meterRes = await _client.PostAsJsonAsync($"/api/sites/{site.Id}/meters", new { serialNumber = "MTR-" + Guid.NewGuid().ToString("N")[..8] });
        meterRes.EnsureSuccessStatusCode();
        return (await meterRes.Content.ReadFromJsonAsync<MeterResponse>())!;
    }
}
