using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class PlanningService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public PlanningService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<List<string>> ValidateAssignmentAsync(int employeeId, int shiftId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var shift = await db.Shifts
            .AsNoTracking()
            .FirstAsync(x => x.Id == shiftId && x.CompanyId == companyId);

        var employee = await db.Employees
            .AsNoTracking()
            .Include(x => x.Qualifications)
            .Include(x => x.AdditionalLocations)
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.CompanyId == companyId);

        var messages = new List<string>();

        if (employee is null || !employee.IsActive)
        {
            messages.Add("Mitarbeiter ist nicht verfügbar.");
            return messages;
        }

        if (shift.Date < employee.HireDate || (employee.LeaveDate is not null && shift.Date > employee.LeaveDate.Value))
            messages.Add("Schicht liegt außerhalb des Beschäftigungszeitraums.");

        if (shift.LocationId is int shiftLocationId)
        {
            var locationAllowed = employee.LocationId == shiftLocationId ||
                                  employee.AdditionalLocations.Any(x => x.LocationId == shiftLocationId);

            if (!locationAllowed)
                messages.Add("Mitarbeiter ist für diesen Standort nicht freigegeben.");
        }

        var absent = await db.Absences.AnyAsync(x =>
            x.CompanyId == companyId &&
            x.EmployeeId == employeeId &&
            x.Status == AbsenceStatus.Approved &&
            x.StartDate <= shift.Date &&
            x.EndDate >= shift.Date);

        if (absent)
            messages.Add("Mitarbeiter ist an diesem Tag abwesend.");

        var nearbyAssignments = await db.ShiftAssignments
            .AsNoTracking()
            .Include(x => x.Shift)
            .Where(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.Shift != null &&
                x.Shift.Date >= shift.Date.AddDays(-1) &&
                x.Shift.Date <= shift.Date.AddDays(1))
            .ToListAsync();

        if (nearbyAssignments.Any(x => x.Shift is not null && Overlaps(x.Shift, shift)))
            messages.Add("Mitarbeiter hat bereits eine überschneidende Schicht.");

        if (nearbyAssignments.Any(x => x.Shift is not null && !Overlaps(x.Shift, shift) && RestHoursBetween(x.Shift, shift) < 11m))
            messages.Add("Die gesetzliche Ruhezeit von 11 Stunden würde unterschritten.");

        var availabilities = await db.Availabilities
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.DayOfWeek == shift.Date.DayOfWeek)
            .ToListAsync();

        if (availabilities.Count > 0)
        {
            var fits = availabilities.Any(x =>
                x.IsAvailable &&
                (x.AvailableFrom == null || x.AvailableFrom <= shift.StartTime) &&
                (x.AvailableUntil == null || x.AvailableUntil >= shift.EndTime));

            if (!fits)
                messages.Add("Schicht liegt außerhalb der Verfügbarkeit.");
        }

        if (shift.ShiftTemplateId is int shiftTemplateId)
        {
            var shiftPreferences = await db.EmployeeShiftPreferences
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId)
                .ToListAsync();

            if (shiftPreferences.Count > 0 &&
                !shiftPreferences.Any(x => x.ShiftTemplateId == shiftTemplateId && x.IsAllowed))
            {
                messages.Add("Mitarbeiter darf diese Schichtart nicht arbeiten.");
            }
        }

        if (shift.RequiredQualificationId is int qualificationId &&
            !employee.Qualifications.Any(x => x.QualificationId == qualificationId))
        {
            messages.Add("Benötigte Qualifikation fehlt.");
        }

        if (employee.WeeklyHours > 0)
        {
            var weekStart = StartOfWeek(shift.Date);
            var weekEnd = weekStart.AddDays(6);

            var weeklyAssignments = await db.ShiftAssignments
                .AsNoTracking()
                .Include(x => x.Shift)
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.EmployeeId == employeeId &&
                    x.Shift != null &&
                    x.Shift.Date >= weekStart &&
                    x.Shift.Date <= weekEnd &&
                    x.ShiftId != shift.Id)
                .ToListAsync();

            var plannedHours = weeklyAssignments
                .Where(x => x.Shift is not null)
                .Sum(x => ShiftHours(x.Shift!));

            if (plannedHours + ShiftHours(shift) > employee.WeeklyHours + 0.01m)
                messages.Add($"Wochenstunden würden überschritten ({plannedHours:0.##} von {employee.WeeklyHours:0.##} Std. bereits geplant).");
        }

        return messages.Distinct().ToList();
    }

    public async Task<List<Employee>> FindReplacementCandidatesAsync(int shiftId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var shift = await db.Shifts.AsNoTracking()
            .FirstAsync(x => x.Id == shiftId && x.CompanyId == companyId);

        var employees = await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .ToListAsync();

        var weekStart = StartOfWeek(shift.Date);
        var weekEnd = weekStart.AddDays(6);

        var weekAssignments = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Shift != null &&
                x.Shift.Date >= weekStart &&
                x.Shift.Date <= weekEnd)
            .ToListAsync();

        var result = new List<(Employee Employee, decimal LoadRatio, decimal PlannedHours)>();

        foreach (var employee in employees)
        {
            var problems = await ValidateAssignmentAsync(employee.Id, shiftId);
            if (problems.Count > 0)
                continue;

            var planned = weekAssignments
                .Where(x => x.EmployeeId == employee.Id && x.Shift is not null)
                .Sum(x => ShiftHours(x.Shift!));

            var ratio = employee.WeeklyHours > 0 ? planned / employee.WeeklyHours : planned;
            result.Add((employee, ratio, planned));
        }

        return result
            .OrderBy(x => x.LoadRatio)
            .ThenBy(x => x.PlannedHours)
            .ThenBy(x => x.Employee.LastName)
            .ThenBy(x => x.Employee.FirstName)
            .Select(x => x.Employee)
            .ToList();
    }

    public async Task AutoPlanAsync(int scheduleId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var schedule = await db.Schedules
            .Include(x => x.Shifts)
            .ThenInclude(x => x.Assignments)
            .FirstAsync(x => x.Id == scheduleId && x.CompanyId == companyId);

        foreach (var shift in schedule.Shifts.OrderBy(x => x.Date).ThenBy(x => x.StartTime))
        {
            while (shift.Assignments.Count < shift.RequiredEmployees)
            {
                var candidates = await FindReplacementCandidatesAsync(shift.Id);
                var alreadyAssigned = shift.Assignments.Select(x => x.EmployeeId).ToHashSet();
                var candidate = candidates.FirstOrDefault(x => !alreadyAssigned.Contains(x.Id));

                if (candidate is null)
                    break;

                var assignment = new ShiftAssignment
                {
                    CompanyId = companyId,
                    ShiftId = shift.Id,
                    EmployeeId = candidate.Id
                };

                db.ShiftAssignments.Add(assignment);
                shift.Assignments.Add(assignment);
                await db.SaveChangesAsync();
            }
        }
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    private static decimal ShiftHours(Shift shift)
    {
        var start = shift.Date.ToDateTime(shift.StartTime);
        var end = shift.Date.ToDateTime(shift.EndTime);
        if (end <= start)
            end = end.AddDays(1);

        var minutes = (decimal)(end - start).TotalMinutes - shift.BreakMinutes;
        return Math.Max(0m, minutes / 60m);
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
