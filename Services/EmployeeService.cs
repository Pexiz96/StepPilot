using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class EmployeeService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public EmployeeService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<List<Employee>> GetAllAsync()
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        return await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Include(x => x.Location)
            .Include(x => x.Department)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToListAsync();
    }

    public async Task SaveAsync(Employee employee)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        employee.CompanyId = companyId;

        if (employee.Id == 0)
        {
            db.Employees.Add(employee);
        }
        else
        {
            var existing = await db.Employees
                .FirstAsync(x => x.Id == employee.Id && x.CompanyId == companyId);

            db.Entry(existing).CurrentValues.SetValues(employee);
        }

        await db.SaveChangesAsync();
    }

    public async Task ToggleActiveAsync(int employeeId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var employee = await db.Employees
            .FirstAsync(x => x.Id == employeeId && x.CompanyId == companyId);

        employee.IsActive = !employee.IsActive;
        await db.SaveChangesAsync();
    }
}
