# Solution notes

## Overview

The solution has three projects:

| Project | Responsibility |
|---|---|
| `EnergyMeter.Core` | Entities (`Site`, `Meter`, `Reading`), `UtcTime` and `ConsumptionCalculator`. No ASP.NET or EF dependency. |
| `EnergyMeter.Api` | HTTP surface (minimal API endpoint groups), request/response contracts, `EnergyDbContext` + migration, `ReadingService`. |
| `EnergyMeter.Tests` | xUnit tests: pure calculator tests, duplicate-rule tests on SQLite, light HTTP tests via `WebApplicationFactory`. |

The domain logic that matters most, the consumption calculation, sits in `Core` as a pure static function. It can be tested without a database or web host.
Everything else is deliberately thin.

## Key decisions

### Minimal APIs over controllers
The API surface is small, about ten routes over three resources, and has no cross-cutting filters. Minimal APIs keep each endpoint as a plain method with its dependencies injected as parameters, with no controller base class or attribute routing.
To stop `Program.cs` from turning into one long file, each resource has its own `Map*Endpoints` extension (`SiteEndpoints`, `MeterEndpoints`, `ReadingEndpoints`).
Handlers return `TypedResults` with `Results<...>` unions. This keeps them unit-testable and gives Swagger accurate response types without `[ProducesResponseType]` attributes.

If the API grew (versioning, many filters, a larger team used to MVC), controllers would be a reasonable switch, and the handlers would move across almost unchanged.

### Resource layout
- Meters are created and listed **under their site**: `/api/sites/{siteId}/meters`.
- Once a meter exists it is addressed **directly**: `/api/meters/{meterId}/readings`, `/api/meters/{meterId}/summary`.
  A meter id is globally unique, so nesting readings under `/sites/{siteId}/meters/{meterId}` would only add a redundant parameter to validate.
- The summary is a sub-resource of the meter (`GET .../summary`) rather than an RPC-style `POST /calculate`. It is a safe, cacheable read.

### Data model
- `Site 1─* Meter 1─* Reading`, foreign keys with cascade delete.
- `Reading.ValueKwh` is `decimal(18,3)`. Money-style decimal avoids floating-point drift when subtracting cumulative values (`12543.7 - 12500.0` must be exactly `43.7`). Three decimal places is Wh resolution.
- `Reading.Id` is `bigint` because readings are the high-volume table.
- Entities are never serialised directly. Request and response records in `Contracts.cs` decouple the wire format from the persistence model and avoid navigation-property cycles.

### Uniqueness rule (one reading per meter per timestamp)
The rule is enforced in two layers, and both produce the same outcome for the client:

1. **Database**: a unique index `UX_Readings_MeterId_TimestampUtc` on `(MeterId, TimestampUtc)`. This is the real guarantee: it holds under concurrent requests and for any other writer. It also serves the "readings for a meter between two times" query, so it is not an extra cost.
2. **Service**: `ReadingService.CreateAsync` checks for an existing reading first so the normal case returns a clean result without an exception.
   If two requests race past the check, the insert hits the index and `DbUpdateException` is caught. The service then re-checks for the row instead of parsing SQL Server error numbers (2601/2627), so the logic is provider-agnostic and the SQLite tests exercise the same path.

Either way the API responds **409 Conflict** with a problem-details body. Duplicates are never silently overwritten (no upsert): a second value for the same instant is more likely a data problem the client should know about.

### Timestamps
- Stored as `datetime2` in UTC. A model-wide EF value converter marks every `DateTime` read back as `DateTimeKind.Utc`, so JSON always ends in `Z` and comparisons are consistent.
- Incoming values go through `UtcTime.Normalize`. An offset (`+02:00`) is converted to UTC; no zone is treated as UTC, per the spec. So `10:00+02:00` and `08:00Z` count as the same instant and conflict.

### Consumption summary
`ConsumptionCalculator.Calculate(readings, fromUtc, toUtc)` implements the boundary rules literally:
- filters to `fromUtc <= t <= toUtc` (inclusive) and orders by timestamp;
- **total** = last − first; 0 if fewer than two readings;
- **daily** = consecutive differences, attributed to the UTC date of the *later* reading, summing positive differences only. Days without a pair are omitted.

The service only loads in-range rows from the database (`WHERE MeterId = @id AND TimestampUtc BETWEEN ...`, covered by the unique index). The calculator filters again anyway, so it is correct on its own and easy to test.

**Decreasing readings (meter reset, optional):** I handle this minimally and transparently rather than guessing:
- negative deltas are excluded from the daily breakdown (this follows from "sum only the positive differences");
- the total still follows the stated rule (latest − earliest), so it can be lower than the sum of daily values, or negative;
- the response includes `hasDecreasingReadings: true` so a client knows the daily figures won't reconcile with the total.

A fuller treatment would model resets explicitly, for example a reset event or a register rollover value per meter, and compute total = Σ positive deltas. I didn't do that because it changes the specified definition of "total".

### Validation and errors
- Request records use nullable properties, so a missing field is reported as "required" rather than silently becoming `0` or `DateTime.MinValue`.
- Hand-written validation returns `ValidationProblem` (field → messages). With this few rules, FluentValidation or a filter pipeline felt like more ceremony than value.
- All errors use RFC 7807 problem details (`AddProblemDetails`): `400` validation, `404` missing parent or resource, `409` duplicate.
- A small `IExceptionHandler` maps `BadHttpRequestException` (for example `fromUtc=not-a-date` or malformed JSON) to `400`. By default `UseExceptionHandler` would have reported these as `500`. I found this while testing manually against the running API.

### Configuration
- Connection string `ConnectionStrings:EnergyDb`, overridable by the env var `ConnectionStrings__EnergyDb` or user-secrets.
- Migrate-on-startup and demo seeding are configuration switches, enabled only in `appsettings.Development.json`. That keeps first-run setup to one command without the production app silently changing schemas.
- `TimeProvider` is injected for `CreatedUtc`, so time is controllable in tests.

### Demo page (bonus, not required by the brief)
`wwwroot/index.html` is a single static page served at `/`. You can pick a site and a meter, add readings, filter them, and see the summary as a daily bar chart. It also shows the API's validation and 409 messages as they are returned.
- It is plain HTML and JavaScript with no framework, build step or CDN dependency, so it adds nothing to setup and runs offline.
- It uses only the public API, the same calls a real client would make, so it doubles as a manual test harness. It adds no endpoints.
- `datetime-local` inputs carry no zone, so the page appends `Z` and treats them as UTC, which matches the API contract.
- I built it to make the video demo easier to follow. If the brief had scored UI, I would have used a proper front-end project.

## Testing

| Test class | Kind | What it proves |
|---|---|---|
| `ConsumptionCalculatorTests` | Pure unit | Multi-day breakdown (pair spanning midnight goes to the later date), inclusive bounds, out-of-range excluded, unordered input, <2 readings ⇒ 0, gap days omitted, decreasing reading excluded and flagged, inverted range rejected. |
| `DuplicateReadingTests` | SQLite in-memory | Duplicate rejected by the service; same instant with a different offset is a duplicate; same timestamp on another meter is allowed; the DB index rejects a duplicate that bypasses the service; a simulated race (insert between pre-check and save) still yields `DuplicateTimestamp`. |
| `ApiEndpointTests` | `WebApplicationFactory` + SQLite | End-to-end create → summary flow, 409 problem details, range-filtered listing, 400 field errors, malformed input ⇒ 400, unknown parents ⇒ 404, demo page served at `/`. |

I used SQLite in-memory rather than the EF in-memory provider because the latter ignores unique indexes, and that would make the duplicate tests meaningless.
SQLite is not SQL Server, though, so I also ran the API against LocalDB to check the migration, seed data, summary, 409 and error responses (see README examples).

```
dotnet test   →   24 passed
```

## Trade-offs and what I'd do next

- **No pagination** on list endpoints. Readings will grow fast, so the next step is keyset pagination on `(TimestampUtc, Id)` with a `limit`, which reuses the existing index.
- **Single-reading inserts only.** Real meters upload in batches. A `POST .../readings:batch` that reports per-item conflicts would be the natural addition.
- **No update/delete**, as allowed by the brief.
- **Meter serial numbers are not unique.** The brief asks for a single uniqueness rule. In a real system I'd likely make serials unique, at least per site.
- **Summary computed in memory.** That's fine for a meter's readings over typical ranges. For very large ranges, the daily aggregation could be pushed into SQL with `LAG()` over the index.
- **Days are UTC calendar days**, per the brief. Customers usually want their local day, so a site time zone would be a likely follow-up.

## AI use

- **Tools:** Claude Code (Anthropic), used in the terminal. I used it to scaffold the solution structure, draft the endpoints, EF configuration and tests, and draft the README/SOLUTION documents, the Mermaid diagrams and the optional demo page. I reviewed and edited every file and ran everything locally.
- **Suggestions I changed or rejected:**
  - The first version of the "is the range inverted?" check called `ToUniversalTime()` on query-string dates. For timestamps without a zone, .NET treats them as *local* time, which would shift the comparison by the machine's UTC offset. I changed it to use the same `UtcTime.Normalize` rule as everywhere else (no zone = UTC).
  - The first version returned **500** for a malformed date in the query string. I caught this while calling the running API by hand and added the `BadHttpRequestException` → 400 handler, plus a test.
  - When the API failed to start locally, the AI first blamed Visual Studio. I ran the API from a plain terminal and got the same error, which showed the real cause was LocalDB failing to start. I rejected the Visual Studio explanation and moved my local setup to SQL Server Express, using user secrets, so the connection string stays out of the repo. LocalDB remains the default in `appsettings.json`.
- **How I checked correctness:**
  - Worked through the summary rules by hand for the test cases (including a pair that spans midnight) before trusting the assertions.
  - Ran the full test suite (24 tests) and checked that the duplicate tests use a relational provider, so they would fail if the unique index were missing.
  - Ran the API against SQL Server Express and exercised every endpoint, including the error paths, to confirm the real SQL Server migration and responses behave as documented.
