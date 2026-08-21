using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public enum ComplianceSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ComplianceFinding(
    string Code,
    ComplianceSeverity Severity,
    string Message,
    string LegalReference);

public sealed class WorkTimeComplianceService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly GermanPublicHolidayService _holidays;

    public WorkTimeComplianceService(
        IDbContextFactory<ApplicationDbContext> factory,
        GermanPublicHolidayService holidays)
    {
        _factory = factory;
        _holidays = holidays;
    }

    public async Task<List<ComplianceFinding>> EvaluateAssignmentAsync(
        int companyId,
        int employeeId,
        Shift candidateShift)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var nearbyAssignments = await db.ShiftAssignments
            .AsNoTracking()
            .Include(x => x.Shift)
            .Where(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.Shift != null &&
                x.ShiftId != candidateShift.Id &&
                x.Shift.Date >= candidateShift.Date.AddDays(-1) &&
                x.Shift.Date <= candidateShift.Date.AddDays(1))
            .ToListAsync();

        var nearbyShifts = nearbyAssignments.Where(x => x.Shift is not null).Select(x => x.Shift!).ToList();

        var historyStart = candidateShift.Date.AddDays(-370);
        var historyAssignments = await db.ShiftAssignments
            .AsNoTracking()
            .Include(x => x.Shift)
            .Where(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.Shift != null &&
                x.ShiftId != candidateShift.Id &&
                x.Shift.Date >= historyStart &&
                x.Shift.Date <= candidateShift.Date.AddDays(56))
            .ToListAsync();

        var historyShifts = historyAssignments.Where(x => x.Shift is not null).Select(x => x.Shift!).ToList();

        Location? location = null;
        if (candidateShift.LocationId is int locationId)
        {
            location = await db.Locations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == locationId && x.CompanyId == companyId);
        }

        var findings = new List<ComplianceFinding>();
        EvaluateBreaks(candidateShift, findings);
        EvaluateDailyWorkingTime(candidateShift, nearbyShifts, findings);
        EvaluateRestPeriod(candidateShift, nearbyShifts, findings);
        EvaluateNightWork(candidateShift, findings);
        EvaluateSundayWork(candidateShift, historyShifts, findings);
        EvaluatePublicHolidayWork(candidateShift, location, historyShifts, findings);

        return findings.GroupBy(x => x.Code).Select(x => x.First()).ToList();
    }

    private static void EvaluateBreaks(Shift shift, List<ComplianceFinding> findings)
    {
        var workingMinutes = WorkingMinutes(shift);
        var requiredBreakMinutes = workingMinutes > 9 * 60 ? 45 : workingMinutes > 6 * 60 ? 30 : 0;
        if (shift.BreakMinutes < requiredBreakMinutes)
            findings.Add(new("ARBZG_BREAK", ComplianceSeverity.Error, $"Für diese Schicht sind mindestens {requiredBreakMinutes} Minuten Ruhepause einzuplanen.", "ArbZG § 4"));
    }

    private static void EvaluateDailyWorkingTime(Shift candidate, IReadOnlyCollection<Shift> existingShifts, List<ComplianceFinding> findings)
    {
        var sameDayMinutes = existingShifts.Where(x => x.Date == candidate.Date).Sum(WorkingMinutes) + WorkingMinutes(candidate);
        if (sameDayMinutes > 10 * 60)
            findings.Add(new("ARBZG_DAILY_MAX", ComplianceSeverity.Error, $"Die geplante Arbeitszeit an diesem Tag beträgt {sameDayMinutes / 60m:0.##} Stunden und überschreitet 10 Stunden.", "ArbZG § 3"));
        else if (sameDayMinutes > 8 * 60)
            findings.Add(new("ARBZG_DAILY_AVERAGE", ComplianceSeverity.Warning, $"Die geplante Arbeitszeit an diesem Tag beträgt {sameDayMinutes / 60m:0.##} Stunden. Mehr als 8 Stunden sind nur im gesetzlichen Ausgleichsrahmen zulässig.", "ArbZG § 3"));
    }

    private static void EvaluateRestPeriod(Shift candidate, IReadOnlyCollection<Shift> existingShifts, List<ComplianceFinding> findings)
    {
        foreach (var existing in existingShifts)
        {
            if (Overlaps(existing, candidate)) continue;
            var restHours = RestHoursBetween(existing, candidate);
            if (restHours < 11m)
            {
                findings.Add(new("ARBZG_REST", ComplianceSeverity.Error, $"Zwischen den Schichten liegen nur {restHours:0.##} Stunden Ruhezeit. Der Standardwert beträgt 11 Stunden.", "ArbZG § 5"));
                break;
            }
        }
    }

    private static void EvaluateNightWork(Shift shift, List<ComplianceFinding> findings)
    {
        var nightMinutes = OverlapMinutesWithDailyWindow(shift, new TimeOnly(23, 0), new TimeOnly(6, 0));
        if (nightMinutes > 2 * 60)
            findings.Add(new("ARBZG_NIGHT_WORK", ComplianceSeverity.Info, $"Die Schicht enthält {nightMinutes / 60m:0.##} Stunden innerhalb der gesetzlichen Nachtzeit. Nacht- und Schichtarbeit erfordert eine besondere arbeitszeitrechtliche Prüfung.", "ArbZG § 2 Abs. 3-5, § 6"));
    }

    private static void EvaluateSundayWork(Shift candidate, IReadOnlyCollection<Shift> historyShifts, List<ComplianceFinding> findings)
    {
        if (candidate.Date.DayOfWeek != DayOfWeek.Sunday) return;

        findings.Add(new("ARBZG_SUNDAY_PERMISSION", ComplianceSeverity.Warning, "Die Schicht liegt an einem Sonntag. Sonntagsbeschäftigung ist grundsätzlich untersagt und nur bei einer gesetzlichen, tariflichen oder behördlich zulässigen Ausnahme einzuplanen.", "ArbZG §§ 9-13"));

        var deadline = candidate.Date.AddDays(13);
        if (!HasFullFreeDay(candidate.Date, deadline, historyShifts, excludeSundays: false))
            findings.Add(new("ARBZG_SUNDAY_REPLACEMENT_REST", ComplianceSeverity.Error, $"Für die Sonntagsbeschäftigung ist bis spätestens {deadline:dd.MM.yyyy} kein vollständig schichtfreier Ersatzruhetag erkennbar.", "ArbZG § 11 Abs. 3"));

        var year = candidate.Date.Year;
        var sundayCount = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Select(d => new DateOnly(year, 1, 1).AddDays(d)).Count(x => x.DayOfWeek == DayOfWeek.Sunday);
        var workedSundays = historyShifts.Where(x => x.Date.Year == year && x.Date.DayOfWeek == DayOfWeek.Sunday).Select(x => x.Date).Append(candidate.Date).Distinct().Count();
        if (sundayCount - workedSundays < 15)
            findings.Add(new("ARBZG_FREE_SUNDAYS", ComplianceSeverity.Error, $"Mit dieser Planung wären nach aktuellem Datenstand nur {sundayCount - workedSundays} beschäftigungsfreie Sonntage im Kalenderjahr übrig. Der gesetzliche Grundsatz verlangt mindestens 15.", "ArbZG § 11 Abs. 1"));
    }

    private void EvaluatePublicHolidayWork(Shift candidate, Location? location, IReadOnlyCollection<Shift> historyShifts, List<ComplianceFinding> findings)
    {
        if (location is null)
        {
            findings.Add(new("HOLIDAY_LOCATION_UNKNOWN", ComplianceSeverity.Info, "Für diese Schicht ist kein Standort hinterlegt. Eine bundeslandabhängige Feiertagsprüfung ist deshalb nicht vollständig möglich.", "ArbZG §§ 9-13"));
            return;
        }

        var holiday = _holidays.GetHoliday(candidate.Date, location.FederalStateCode);
        if (holiday is null) return;

        findings.Add(new("ARBZG_HOLIDAY_PERMISSION", ComplianceSeverity.Warning, $"Die Schicht liegt am gesetzlichen Feiertag „{holiday.Name}“ ({holiday.LegalScope}). Feiertagsbeschäftigung ist grundsätzlich untersagt und benötigt eine zulässige Ausnahme.", "ArbZG §§ 9-13"));

        // Fällt der Feiertag auf einen Sonntag, greift die Sonntagsprüfung bereits mit der engeren Zwei-Wochen-Frist.
        if (candidate.Date.DayOfWeek == DayOfWeek.Sunday) return;

        var deadline = candidate.Date.AddDays(55);
        if (!HasFullFreeDay(candidate.Date, deadline, historyShifts, excludeSundays: true))
            findings.Add(new("ARBZG_HOLIDAY_REPLACEMENT_REST", ComplianceSeverity.Error, $"Für die Arbeit am Feiertag „{holiday.Name}“ ist bis spätestens {deadline:dd.MM.yyyy} kein vollständig arbeitsfreier Werktag als möglicher Ersatzruhetag erkennbar.", "ArbZG § 11 Abs. 3"));
        else
            findings.Add(new("ARBZG_HOLIDAY_REPLACEMENT_REST", ComplianceSeverity.Info, $"Für die Feiertagsarbeit am „{holiday.Name}“ wurde im Acht-Wochen-Zeitraum mindestens ein vollständig schichtfreier Werktag erkannt. Die tatsächliche Gewährung als Ersatzruhetag bleibt organisatorisch zu bestätigen.", "ArbZG § 11 Abs. 3"));
    }

    private static bool HasFullFreeDay(DateOnly workDate, DateOnly deadline, IReadOnlyCollection<Shift> shifts, bool excludeSundays)
    {
        for (var date = workDate.AddDays(1); date <= deadline; date = date.AddDays(1))
        {
            if (excludeSundays && date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (shifts.All(x => x.Date != date)) return true;
        }
        return false;
    }

    private static int OverlapMinutesWithDailyWindow(Shift shift, TimeOnly windowStart, TimeOnly windowEnd)
    {
        var shiftInterval = Interval(shift);
        var totalMinutes = 0d;
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var date = shift.Date.AddDays(dayOffset);
            var start = date.ToDateTime(windowStart);
            var end = date.ToDateTime(windowEnd);
            if (end <= start) end = end.AddDays(1);
            var overlapStart = shiftInterval.Start > start ? shiftInterval.Start : start;
            var overlapEnd = shiftInterval.End < end ? shiftInterval.End : end;
            if (overlapEnd > overlapStart) totalMinutes += (overlapEnd - overlapStart).TotalMinutes;
        }
        return (int)Math.Round(totalMinutes);
    }

    private static int WorkingMinutes(Shift shift)
    {
        var interval = Interval(shift);
        return Math.Max(0, (int)Math.Round((interval.End - interval.Start).TotalMinutes) - Math.Max(0, shift.BreakMinutes));
    }

    private static (DateTime Start, DateTime End) Interval(Shift shift)
    {
        var start = shift.Date.ToDateTime(shift.StartTime);
        var end = shift.Date.ToDateTime(shift.EndTime);
        if (end <= start) end = end.AddDays(1);
        return (start, end);
    }

    private static bool Overlaps(Shift a, Shift b)
    {
        var ai = Interval(a); var bi = Interval(b);
        return ai.Start < bi.End && ai.End > bi.Start;
    }

    private static decimal RestHoursBetween(Shift a, Shift b)
    {
        var ai = Interval(a); var bi = Interval(b);
        if (ai.Start < bi.End && ai.End > bi.Start) return 0m;
        return (decimal)(ai.End <= bi.Start ? bi.Start - ai.End : ai.Start - bi.End).TotalHours;
    }
}
