using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class DemoDataService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DemoDataService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task SeedAsync(int companyId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        if (await db.Employees.AnyAsync(x => x.CompanyId == companyId))
            return;

        var location = new Location
        {
            CompanyId = companyId,
            Name = "Hauptstandort",
            City = "Berlin"
        };

        var department = new Department
        {
            CompanyId = companyId,
            Name = "Betrieb"
        };

        db.AddRange(location, department);
        await db.SaveChangesAsync();

        db.Employees.AddRange(
            new Employee { CompanyId = companyId, EmployeeNumber = "1001", FirstName = "Anna", LastName = "Schmidt", WeeklyHours = 35, Position = "Schichtleitung", LocationId = location.Id, DepartmentId = department.Id },
            new Employee { CompanyId = companyId, EmployeeNumber = "1002", FirstName = "Max", LastName = "Müller", WeeklyHours = 40, Position = "Mitarbeiter", LocationId = location.Id, DepartmentId = department.Id },
            new Employee { CompanyId = companyId, EmployeeNumber = "1003", FirstName = "Lisa", LastName = "Weber", WeeklyHours = 30, Position = "Mitarbeiter", LocationId = location.Id, DepartmentId = department.Id }
        );

        await db.SaveChangesAsync();
    }
}
