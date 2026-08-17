using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class DashboardService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public DashboardService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<DashboardStats> GetAsync()
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekEnd = today.AddDays(7);

        var employees = await db.Employees.CountAsync(x => x.CompanyId == companyId && x.IsActive);
        var todayShifts = await db.Shifts.CountAsync(x => x.CompanyId == companyId && x.Date == today);
        var absent = await db.Absences.CountAsync(x => x.CompanyId == companyId && x.Status == AbsenceStatus.Approved && x.StartDate <= today && x.EndDate >= today);
        var pendingAbsences = await db.Absences.CountAsync(x => x.CompanyId == companyId && x.Status == AbsenceStatus.Requested);

        var upcoming = await db.Shifts
            .Where(x => x.CompanyId == companyId && x.Date >= today && x.Date <= weekEnd)
            .Select(x => new { x.RequiredEmployees, Assigned = x.Assignments.Count })
            .ToListAsync();

        var open = upcoming.Sum(x => Math.Max(0, x.RequiredEmployees - x.Assigned));
        var required = upcoming.Sum(x => x.RequiredEmployees);
        var assigned = upcoming.Sum(x => Math.Min(x.Assigned, x.RequiredEmployees));
        var coverage = required > 0 ? (decimal)assigned / required * 100m : 100m;

        var publishedWeeks = await db.Schedules.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Published && x.WeekStart >= today.AddDays(-7));

        return new DashboardStats(employees, todayShifts, absent, open, pendingAbsences, coverage, publishedWeeks);
    }
}

public sealed record DashboardStats(
    int Employees,
    int TodayShifts,
    int Absent,
    int OpenShifts,
    int PendingAbsences,
    decimal Coverage,
    int PublishedWeeks);
