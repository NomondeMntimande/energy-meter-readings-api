using EnergyMeter.Api.Data;
using EnergyMeter.Api.Endpoints;
using EnergyMeter.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Override with env var ConnectionStrings__EnergyDb or user-secrets; see README.
var connectionString = builder.Configuration.GetConnectionString("EnergyDb")
    ?? throw new InvalidOperationException("Connection string 'EnergyDb' is not configured.");

// Retry transient SQL errors (e.g. LocalDB waking up after idling, a brief network drop)
// instead of surfacing them to the client as a 500.
builder.Services.AddDbContext<EnergyDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));
builder.Services.AddScoped<ReadingService>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Optional demo page (wwwroot/index.html) that calls the API below. Not part of the API contract.
app.UseDefaultFiles();
app.UseStaticFiles();

await DbInitializer.InitializeAsync(app.Services, app.Configuration);

app.MapGroup("/api")
    .MapSiteEndpoints()
    .MapMeterEndpoints()
    .MapReadingEndpoints();

app.Run();

// Exposed for WebApplicationFactory in the test project.
public partial class Program;
