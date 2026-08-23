using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class EmployeeService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;
    private readonly AuditLogService _auditLog;
    private readonly CurrentUserService _currentUser;

    public EmployeeService(
        IDbContextFactory<ApplicationDbContext> factory,
        TenantGuard tenant,
        AuditLogService auditLog,
        CurrentUserService currentUser)
    {
        _factory = factory;
        _tenant = tenant;
        _auditLog = auditLog;
        _currentUser = currentUser;
    }

    public async Task<List<Employee>> GetAllAsync()
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        return await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Include(x => x.Location)
            .Include(x => x.AdditionalLocations).ThenInclude(x => x.Location)
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

        if (employee.LocationId is int locationId && !await db.Locations.AnyAsync(x => x.Id == locationId && x.CompanyId == companyId && x.IsActive))
            throw new InvalidOperationException("Der ausgewählte Hauptstandort ist ungültig.");

        if (employee.DepartmentId is int departmentId && !await db.Departments.AnyAsync(x => x.Id == departmentId && x.CompanyId == companyId && x.IsActive))
            throw new InvalidOperationException("Die ausgewählte Abteilung ist ungültig.");

        var action = employee.Id == 0 ? "Create" : "Update";
        string summary;

        if (employee.Id == 0)
        {
            db.Employees.Add(employee);
            summary = $"Mitarbeiter angelegt; Personalnummer={employee.EmployeeNumber}.";
        }
        else
        {
            var existing = await db.Employees.FirstAsync(x => x.Id == employee.Id && x.CompanyId == companyId);
            var changes = new List<string>();
            AddChange(changes, "Personalnummer", existing.EmployeeNumber, employee.EmployeeNumber);
            AddChange(changes, "Vorname", existing.FirstName, employee.FirstName);
            AddChange(changes, "Nachname", existing.LastName, employee.LastName);
            AddChange(changes, "E-Mail", existing.Email, employee.Email);
            AddChange(changes, "Position", existing.Position, employee.Position);
            AddChange(changes, "Wochenstunden", existing.WeeklyHours, employee.WeeklyHours);
            AddChange(changes, "Hauptstandort", existing.LocationId, employee.LocationId);
            AddChange(changes, "Abteilung", existing.DepartmentId, employee.DepartmentId);
            AddChange(changes, "Aktiv", existing.IsActive, employee.IsActive);
            db.Entry(existing).CurrentValues.SetValues(employee);
            summary = changes.Count == 0 ? "Mitarbeiter gespeichert; keine Stammdatenänderung erkannt." : $"Mitarbeiter geändert: {string.Join("; ", changes)}";
        }

        await db.SaveChangesAsync();
        await _auditLog.WriteAsync("Employee", action, nameof(Employee), employee.Id.ToString(), summary, (await _currentUser.GetUserAsync())?.Id);
    }

    public async Task SaveAdditionalLocationsAsync(int employeeId, IEnumerable<int> locationIds)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var employee = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == employeeId && x.CompanyId == companyId);
        var requested = locationIds.Where(x => employee.LocationId != x).Distinct().ToHashSet();
        var validIds = await db.Locations.Where(x => x.CompanyId == companyId && x.IsActive && requested.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var existing = await db.EmployeeLocations.Where(x => x.EmployeeId == employeeId && x.CompanyId == companyId).ToListAsync();
        var previousIds = existing.Select(x => x.LocationId).OrderBy(x => x).ToArray();

        db.EmployeeLocations.RemoveRange(existing);
        foreach (var locationId in validIds)
        {
            db.EmployeeLocations.Add(new EmployeeLocation { CompanyId = companyId, EmployeeId = employeeId, LocationId = locationId });
        }
        await db.SaveChangesAsync();

        var newIds = validIds.OrderBy(x => x).ToArray();
        if (!previousIds.SequenceEqual(newIds))
            await _auditLog.WriteAsync("Employee", "Locations", nameof(Employee), employeeId.ToString(), $"Zusätzliche Standorte geändert: vorher [{string.Join(',', previousIds)}], nachher [{string.Join(',', newIds)}].", (await _currentUser.GetUserAsync())?.Id);
    }

    public async Task ToggleActiveAsync(int employeeId)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var employee = await db.Employees.FirstAsync(x => x.Id == employeeId && x.CompanyId == companyId);
        employee.IsActive = !employee.IsActive;
        await db.SaveChangesAsync();
        await _auditLog.WriteAsync("Employee", employee.IsActive ? "Activate" : "Deactivate", nameof(Employee), employee.Id.ToString(), $"Mitarbeiterstatus auf {(employee.IsActive ? "aktiv" : "inaktiv")} gesetzt.", (await _currentUser.GetUserAsync())?.Id);
    }

    private static void AddChange<T>(List<string> changes, string field, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after)) changes.Add($"{field}: '{before}' → '{after}'");
    }
}
