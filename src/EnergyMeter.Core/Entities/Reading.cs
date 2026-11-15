namespace EnergyMeter.Core.Entities;

/// <summary>
/// A cumulative register value (kWh) captured at a point in time.
/// Unique per meter + timestamp.
/// </summary>
public class Reading
{
    public long Id { get; set; }
    public int MeterId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal ValueKwh { get; set; }

    public Meter? Meter { get; set; }
}
