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

    public WorkTimeComplianceService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<ComplianceFinding>> EvaluateAssignmentAsync(
        int companyId,
        int employeeId,
        Shift candidateShift)
    {
        await using var db = await _factory.CreateDbContextAsync();

        // Für Ruhezeiten genügt der direkte Zeitraum um die Kandidatenschicht.
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

        var nearbyShifts = nearbyAssignments
            .Where(x => x.Shift is not null)
            .Select(x => x.Shift!)
            .ToList();

        // Für Sonntagsregeln brauchen wir einen längeren Rückblick.
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

        var historyShifts = historyAssignments
            .Where(x => x.Shift is not null)
            .Select(x => x.Shift!)
            .ToList();

        var findings = new List<ComplianceFinding>();

        EvaluateBreaks(candidateShift, findings);
        EvaluateDailyWorkingTime(candidateShift, nearbyShifts, findings);
        EvaluateRestPeriod(candidateShift, nearbyShifts, findings);
        EvaluateNightWork(candidateShift, findings);
        EvaluateSundayWork(candidateShift, historyShifts, findings);

        return findings
            .GroupBy(x => x.Code)
            .Select(x => x.First())
            .ToList();
    }

    private static void EvaluateBreaks(Shift shift, List<ComplianceFinding> findings)
    {
        var workingMinutes = WorkingMinutes(shift);
        var requiredBreakMinutes = workingMinutes > 9 * 60
            ? 45
            : workingMinutes > 6 * 60
                ? 30
                : 0;

        if (shift.BreakMinutes < requiredBreakMinutes)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_BREAK",
                ComplianceSeverity.Error,
                $"Für diese Schicht sind mindestens {requiredBreakMinutes} Minuten Ruhepause einzuplanen.",
                "ArbZG § 4"));
        }
    }

    private static void EvaluateDailyWorkingTime(
        Shift candidate,
        IReadOnlyCollection<Shift> existingShifts,
        List<ComplianceFinding> findings)
    {
        var sameDayMinutes = existingShifts
            .Where(x => x.Date == candidate.Date)
            .Sum(WorkingMinutes) + WorkingMinutes(candidate);

        if (sameDayMinutes > 10 * 60)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_DAILY_MAX",
                ComplianceSeverity.Error,
                $"Die geplante Arbeitszeit an diesem Tag beträgt {sameDayMinutes / 60m:0.##} Stunden und überschreitet 10 Stunden.",
                "ArbZG § 3"));
        }
        else if (sameDayMinutes > 8 * 60)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_DAILY_AVERAGE",
                ComplianceSeverity.Warning,
                $"Die geplante Arbeitszeit an diesem Tag beträgt {sameDayMinutes / 60m:0.##} Stunden. Mehr als 8 Stunden sind nur im gesetzlichen Ausgleichsrahmen zulässig.",
                "ArbZG § 3"));
        }
    }

    private static void EvaluateRestPeriod(
        Shift candidate,
        IReadOnlyCollection<Shift> existingShifts,
        List<ComplianceFinding> findings)
    {
        foreach (var existing in existingShifts)
        {
            if (Overlaps(existing, candidate))
                continue;

            var restHours = RestHoursBetween(existing, candidate);
            if (restHours < 11m)
            {
                findings.Add(new ComplianceFinding(
                    "ARBZG_REST",
                    ComplianceSeverity.Error,
                    $"Zwischen den Schichten liegen nur {restHours:0.##} Stunden Ruhezeit. Der Standardwert beträgt 11 Stunden.",
                    "ArbZG § 5"));
                break;
            }
        }
    }

    private static void EvaluateNightWork(Shift shift, List<ComplianceFinding> findings)
    {
        var nightMinutes = OverlapMinutesWithDailyWindow(shift, new TimeOnly(23, 0), new TimeOnly(6, 0));

        if (nightMinutes > 2 * 60)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_NIGHT_WORK",
                ComplianceSeverity.Info,
                $"Die Schicht enthält {nightMinutes / 60m:0.##} Stunden innerhalb der gesetzlichen Nachtzeit. Nacht- und Schichtarbeit erfordert eine besondere arbeitszeitrechtliche Prüfung.",
                "ArbZG § 2 Abs. 3-5, § 6"));
        }
    }

    private static void EvaluateSundayWork(
        Shift candidate,
        IReadOnlyCollection<Shift> historyShifts,
        List<ComplianceFinding> findings)
    {
        if (candidate.Date.DayOfWeek != DayOfWeek.Sunday)
            return;

        findings.Add(new ComplianceFinding(
            "ARBZG_SUNDAY_PERMISSION",
            ComplianceSeverity.Warning,
            "Die Schicht liegt an einem Sonntag. Sonntagsbeschäftigung ist grundsätzlich untersagt und nur bei einer gesetzlichen, tariflichen oder behördlich zulässigen Ausnahme einzuplanen.",
            "ArbZG §§ 9-13"));

        var replacementDeadline = candidate.Date.AddDays(13);
        var hasReplacementRestDay = Enumerable.Range(1, 13)
            .Select(candidate.Date.AddDays)
            .Any(date => historyShifts.All(x => x.Date != date));

        if (!hasReplacementRestDay)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_SUNDAY_REPLACEMENT_REST",
                ComplianceSeverity.Error,
                $"Für die Sonntagsbeschäftigung ist bis spätestens {replacementDeadline:dd.MM.yyyy} kein freier Ersatzruhetag erkennbar.",
                "ArbZG § 11 Abs. 3"));
        }
        else
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_SUNDAY_REPLACEMENT_REST",
                ComplianceSeverity.Info,
                "Für Sonntagsarbeit muss innerhalb des gesetzlichen Zwei-Wochen-Zeitraums ein Ersatzruhetag gewährleistet sein. StepPilot hat im aktuellen Plan mindestens einen schichtfreien Tag erkannt.",
                "ArbZG § 11 Abs. 3"));
        }

        var year = candidate.Date.Year;
        var sundaysInYear = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365)
            .Select(day => new DateOnly(year, 1, 1).AddDays(day))
            .Where(x => x.DayOfWeek == DayOfWeek.Sunday)
            .ToList();

        var workedSundays = historyShifts
            .Where(x => x.Date.Year == year && x.Date.DayOfWeek == DayOfWeek.Sunday)
            .Select(x => x.Date)
            .Append(candidate.Date)
            .Distinct()
            .Count();

        var freeSundays = sundaysInYear.Count - workedSundays;
        if (freeSundays < 15)
        {
            findings.Add(new ComplianceFinding(
                "ARBZG_FREE_SUNDAYS",
                ComplianceSeverity.Error,
                $"Mit dieser Planung wären nach aktuellem Datenstand nur {freeSundays} beschäftigungsfreie Sonntage im Kalenderjahr übrig. Der gesetzliche Grundsatz verlangt mindestens 15.",
                "ArbZG § 11 Abs. 1"));
        }
    }

    private static int OverlapMinutesWithDailyWindow(Shift shift, TimeOnly windowStart, TimeOnly windowEnd)
    {
        var shiftInterval = Interval(shift);
        var totalMinutes = 0d;

        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var date = shift.Date.AddDays(dayOffset);
            var windowStartDateTime = date.ToDateTime(windowStart);
            var windowEndDateTime = date.ToDateTime(windowEnd);
            if (windowEndDateTime <= windowStartDateTime)
                windowEndDateTime = windowEndDateTime.AddDays(1);

            var overlapStart = shiftInterval.Start > windowStartDateTime ? shiftInterval.Start : windowStartDateTime;
            var overlapEnd = shiftInterval.End < windowEndDateTime ? shiftInterval.End : windowEndDateTime;

            if (overlapEnd > overlapStart)
                totalMinutes += (overlapEnd - overlapStart).TotalMinutes;
        }

        return (int)Math.Round(totalMinutes);
    }

    private static int WorkingMinutes(Shift shift)
    {
        var interval = Interval(shift);
        var grossMinutes = (int)Math.Round((interval.End - interval.Start).TotalMinutes);
        return Math.Max(0, grossMinutes - Math.Max(0, shift.BreakMinutes));
    }

    private static (DateTime Start, DateTime End) Interval(Shift shift)
    {
        var start = shift.Date.ToDateTime(shift.StartTime);
        var end = shift.Date.ToDateTime(shift.EndTime);
        if (end <= start)
            end = end.AddDays(1);
        return (start, end);
    }

    private static bool Overlaps(Shift a, Shift b)
    {
        var ai = Interval(a);
        var bi = Interval(b);
        return ai.Start < bi.End && ai.End > bi.Start;
    }

    private static decimal RestHoursBetween(Shift a, Shift b)
    {
        var ai = Interval(a);
        var bi = Interval(b);
        if (ai.Start < bi.End && ai.End > bi.Start)
            return 0m;

        var gap = ai.End <= bi.Start ? bi.Start - ai.End : ai.Start - bi.End;
        return (decimal)gap.TotalHours;
    }
}
