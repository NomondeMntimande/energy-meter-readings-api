namespace EnergyMeter.Core.Time;

public static class UtcTime
{
    /// <summary>
    /// Normalises an incoming timestamp to UTC.
    /// Values with an offset (Kind = Local after parsing) are converted; values without
    /// any zone information are treated as already being UTC, as the API contract states.
    /// </summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
