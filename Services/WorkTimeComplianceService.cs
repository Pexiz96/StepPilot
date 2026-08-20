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

        var relevantAssignments = await db.ShiftAssignments
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

        var existingShifts = relevantAssignments
            .Where(x => x.Shift is not null)
            .Select(x => x.Shift!)
            .ToList();

        var findings = new List<ComplianceFinding>();

        EvaluateBreaks(candidateShift, findings);
        EvaluateDailyWorkingTime(candidateShift, existingShifts, findings);
        EvaluateRestPeriod(candidateShift, existingShifts, findings);

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
