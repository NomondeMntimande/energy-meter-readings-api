using EnergyMeter.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EnergyMeter.Api.Data;

public class EnergyDbContext(DbContextOptions<EnergyDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server datetime2 has no zone, so mark everything coming back as UTC.
        // This makes JSON output end in "Z" and keeps comparisons consistent.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>(site =>
        {
            site.Property(s => s.Name).HasMaxLength(200).IsRequired();
            site.HasMany(s => s.Meters)
                .WithOne(m => m.Site)
                .HasForeignKey(m => m.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Meter>(meter =>
        {
            meter.Property(m => m.SerialNumber).HasMaxLength(100).IsRequired();
            meter.HasMany(m => m.Readings)
                .WithOne(r => r.Meter)
                .HasForeignKey(r => r.MeterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Reading>(reading =>
        {
            reading.Property(r => r.ValueKwh).HasPrecision(18, 3);

            // The uniqueness rule: one reading per meter per timestamp.
            // The index also serves the "readings for a meter in a time range" query.
            reading.HasIndex(r => new { r.MeterId, r.TimestampUtc })
                .IsUnique()
                .HasDatabaseName("UX_Readings_MeterId_TimestampUtc");
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
