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
            .Include(x => x.AdditionalLocations)
                .ThenInclude(x => x.Location)
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

        if (employee.LocationId is int locationId)
        {
            var validLocation = await db.Locations.AnyAsync(x =>
                x.Id == locationId &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (!validLocation)
                throw new InvalidOperationException("Der ausgewählte Hauptstandort ist ungültig.");
        }

        if (employee.DepartmentId is int departmentId)
        {
            var validDepartment = await db.Departments.AnyAsync(x =>
                x.Id == departmentId &&
                x.CompanyId == companyId &&
                x.IsActive);

            if (!validDepartment)
                throw new InvalidOperationException("Die ausgewählte Abteilung ist ungültig.");
        }

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

    public async Task SaveAdditionalLocationsAsync(int employeeId, IEnumerable<int> locationIds)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var employee = await db.Employees
            .AsNoTracking()
            .FirstAsync(x => x.Id == employeeId && x.CompanyId == companyId);

        var requested = locationIds
            .Where(x => employee.LocationId != x)
            .Distinct()
            .ToHashSet();

        var validIds = await db.Locations
            .Where(x => x.CompanyId == companyId && x.IsActive && requested.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        var existing = await db.EmployeeLocations
            .Where(x => x.EmployeeId == employeeId && x.CompanyId == companyId)
            .ToListAsync();

        db.EmployeeLocations.RemoveRange(existing);

        foreach (var locationId in validIds)
        {
            db.EmployeeLocations.Add(new EmployeeLocation
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LocationId = locationId
            });
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
