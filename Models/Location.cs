using StepPilot.Data;

namespace StepPilot.Models;

public class Location : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }

    // ISO 3166-2 subdivision code, e.g. DE-BE, DE-BY, DE-ST.
    // Used by compliance services for location-specific public-holiday rules.
    public string? FederalStateCode { get; set; }

    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }

    // Mitarbeiter, deren Hauptstandort dieser Standort ist.
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    // Mitarbeiter, die zusätzlich für diesen Standort freigegeben sind.
    public ICollection<EmployeeLocation> AdditionalEmployees { get; set; } = new List<EmployeeLocation>();
}
