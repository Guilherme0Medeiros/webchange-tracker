namespace WebChangeRadar.Api.Features.Monitors;

public sealed class CheckSchedule(IConfiguration configuration, IHostEnvironment environment)
{
    public int? DevelopmentIntervalMinutes
    {
        get
        {
            if (!environment.IsDevelopment()) return null;
            return int.TryParse(configuration["DevelopmentCheckIntervalMinutes"], out var value) &&
                value is >= 1 and < 60 ? value : null;
        }
    }

    public DateTime? NextCheckAt(DateTime? regularNextCheckAt, int intervalMinutes)
    {
        if (regularNextCheckAt is null) return null;
        var effective = DevelopmentIntervalMinutes;
        return effective is null ? regularNextCheckAt :
            regularNextCheckAt.Value.AddMinutes(effective.Value - intervalMinutes);
    }
}
