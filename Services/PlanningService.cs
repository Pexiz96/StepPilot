using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class PlanningService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public PlanningService(
        IDbContextFactory<ApplicationDbContext> factory,
        TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<List<string>> ValidateAssignmentAsync(
        int employeeId,
        int shiftId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();

        await using var db =
            await _factory.CreateDbContextAsync();

        var shift = await db.Shifts
            .FirstAsync(x =>
                x.Id == shiftId &&
                x.CompanyId == companyId);

        var employee = await db.Employees
            .Include(x => x.Qualifications)
            .FirstOrDefaultAsync(x =>
                x.Id == employeeId &&
                x.CompanyId == companyId);

        var messages = new List<string>();

        if (employee is null || !employee.IsActive)
        {
            messages.Add(
                "Mitarbeiter ist nicht verfügbar.");

            return messages;
        }

        // Urlaub / Krankheit / sonstige Abwesenheit
        var absent = await db.Absences
            .AnyAsync(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.Status == AbsenceStatus.Approved &&
                x.StartDate <= shift.Date &&
                x.EndDate >= shift.Date);

        if (absent)
        {
            messages.Add(
                "Mitarbeiter ist an diesem Tag abwesend.");
        }

        // Überschneidende Schicht
        var overlap = await db.ShiftAssignments
            .Include(x => x.Shift)
            .AnyAsync(x =>
                x.CompanyId == companyId &&
                x.EmployeeId == employeeId &&
                x.Shift != null &&
                x.Shift.Date == shift.Date &&
                x.Shift.StartTime < shift.EndTime &&
                x.Shift.EndTime > shift.StartTime);

        if (overlap)
        {
            messages.Add(
                "Mitarbeiter hat bereits eine überschneidende Schicht.");
        }

        // Persönliche zeitliche Verfügbarkeit
        var availabilities =
            await db.Availabilities
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.EmployeeId == employeeId &&
                    x.DayOfWeek == shift.Date.DayOfWeek)
                .ToListAsync();

        if (availabilities.Count > 0)
        {
            var fits = availabilities.Any(x =>
                x.IsAvailable &&
                (x.AvailableFrom == null ||
                 x.AvailableFrom <= shift.StartTime) &&
                (x.AvailableUntil == null ||
                 x.AvailableUntil >= shift.EndTime));

            if (!fits)
            {
                messages.Add(
                    "Schicht liegt außerhalb der Verfügbarkeit.");
            }
        }

        // Darf der Mitarbeiter diese Schichtart arbeiten?
        if (shift.ShiftTemplateId is int shiftTemplateId)
        {
            var shiftPreferences =
                await db.EmployeeShiftPreferences
                    .Where(x =>
                        x.EmployeeId == employeeId)
                    .ToListAsync();

            // Wenn keine Präferenzen existieren:
            // zunächst alle Schichten erlaubt.
            if (shiftPreferences.Count > 0)
            {
                var isAllowed =
                    shiftPreferences.Any(x =>
                        x.ShiftTemplateId == shiftTemplateId &&
                        x.IsAllowed);

                if (!isAllowed)
                {
                    messages.Add(
                        "Mitarbeiter darf diese Schichtart nicht arbeiten.");
                }
            }
        }

        // Benötigte Qualifikation
        if (shift.RequiredQualificationId is int qualificationId)
        {
            var hasQualification =
                employee.Qualifications.Any(x =>
                    x.QualificationId == qualificationId);

            if (!hasQualification)
            {
                messages.Add(
                    "Benötigte Qualifikation fehlt.");
            }
        }

        return messages;
    }

    public async Task<List<Employee>>
        FindReplacementCandidatesAsync(int shiftId)
    {
        var companyId =
            await _tenant.RequireCompanyIdAsync();

        await using var db =
            await _factory.CreateDbContextAsync();

        var employees =
            await db.Employees
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.IsActive)
                .ToListAsync();

        var result =
            new List<(Employee Employee, int Warnings)>();

        foreach (var employee in employees)
        {
            var warnings =
                await ValidateAssignmentAsync(
                    employee.Id,
                    shiftId);

            var blocked = warnings.Any(x =>
                x.Contains(
                    "abwesend",
                    StringComparison.OrdinalIgnoreCase) ||

                x.Contains(
                    "überschneidende",
                    StringComparison.OrdinalIgnoreCase) ||

                x.Contains(
                    "außerhalb",
                    StringComparison.OrdinalIgnoreCase) ||

                x.Contains(
                    "Qualifikation fehlt",
                    StringComparison.OrdinalIgnoreCase) ||

                x.Contains(
                    "Schichtart nicht arbeiten",
                    StringComparison.OrdinalIgnoreCase));

            if (!blocked)
            {
                result.Add(
                    (employee, warnings.Count));
            }
        }

        return result
            .OrderBy(x => x.Warnings)
            .ThenBy(x => x.Employee.LastName)
            .ThenBy(x => x.Employee.FirstName)
            .Select(x => x.Employee)
            .ToList();
    }

    public async Task AutoPlanAsync(int scheduleId)
    {
        var companyId =
            await _tenant.RequireCompanyIdAsync();

        await using var db =
            await _factory.CreateDbContextAsync();

        var schedule = await db.Schedules
            .Include(x => x.Shifts)
            .ThenInclude(x => x.Assignments)
            .FirstAsync(x =>
                x.Id == scheduleId &&
                x.CompanyId == companyId);

        foreach (var shift in schedule.Shifts
                     .OrderBy(x => x.Date)
                     .ThenBy(x => x.StartTime))
        {
            while (shift.Assignments.Count <
                   shift.RequiredEmployees)
            {
                var candidates =
                    await FindReplacementCandidatesAsync(
                        shift.Id);

                var alreadyAssigned =
                    shift.Assignments
                        .Select(x => x.EmployeeId)
                        .ToHashSet();

                var candidate =
                    candidates.FirstOrDefault(x =>
                        !alreadyAssigned.Contains(x.Id));

                if (candidate is null)
                {
                    break;
                }

                var assignment =
                    new ShiftAssignment
                    {
                        CompanyId = companyId,
                        ShiftId = shift.Id,
                        EmployeeId = candidate.Id
                    };

                db.ShiftAssignments.Add(
                    assignment);

                shift.Assignments.Add(
                    assignment);

                await db.SaveChangesAsync();
            }
        }
    }
}