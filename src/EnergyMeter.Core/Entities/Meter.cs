namespace EnergyMeter.Core.Entities;

public class Meter
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public required string SerialNumber { get; set; }
    public DateTime CreatedUtc { get; set; }

    public Site? Site { get; set; }
    public List<Reading> Readings { get; set; } = [];
}
