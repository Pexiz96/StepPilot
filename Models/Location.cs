using StepPilot.Data;

namespace StepPilot.Models;

public class Location : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
