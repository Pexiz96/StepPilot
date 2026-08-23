namespace StepPilot.Services;

public static class TimeTrackingRules
{
    public static int CalculateWorkedMinutes(DateTime clockInUtc, DateTime clockOutUtc, int breakMinutes)
    {
        if (clockOutUtc <= clockInUtc) return 0;
        var gross = (int)Math.Round((clockOutUtc - clockInUtc).TotalMinutes);
        return Math.Max(0, gross - Math.Max(0, breakMinutes));
    }

    public static int RequiredBreakMinutes(int grossMinutes, int after6HoursMinutes, int after9HoursMinutes)
    {
        if (grossMinutes > 9 * 60) return Math.Max(0, after9HoursMinutes);
        if (grossMinutes > 6 * 60) return Math.Max(0, after6HoursMinutes);
        return 0;
    }

    public static decimal RestHours(DateTime previousClockOutUtc, DateTime nextClockInUtc)
    {
        if (nextClockInUtc <= previousClockOutUtc) return 0m;
        return (decimal)(nextClockInUtc - previousClockOutUtc).TotalHours;
    }
}
