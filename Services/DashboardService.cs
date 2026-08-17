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

        var employees = await db.Employees.CountAsync(x => x.CompanyId == companyId && x.IsActive);
        var shifts = await db.Shifts.CountAsync(x => x.CompanyId == companyId && x.Date == today);
        var absent = await db.Absences.CountAsync(x =>
            x.CompanyId == companyId &&
            x.Status == AbsenceStatus.Approved &&
            x.StartDate <= today &&
            x.EndDate >= today);

        var shiftsWithAssignments = await db.Shifts
            .Where(x => x.CompanyId == companyId && x.Date >= today)
            .Select(x => new
            {
            x.RequiredEmployees,
            Assigned = x.Assignments.Count
            })
            .ToListAsync();

        var open = shiftsWithAssignments
            .Sum(x => Math.Max(0, x.RequiredEmployees - x.Assigned));

        return new DashboardStats(employees, shifts, absent, open);
    }
}

public sealed record DashboardStats(int Employees, int TodayShifts, int Absent, int OpenShifts);
