namespace EnergyMeter.Core.Entities;

public class Site
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedUtc { get; set; }

    public List<Meter> Meters { get; set; } = [];
}
