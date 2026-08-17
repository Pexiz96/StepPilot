using StepPilot.Data;

namespace StepPilot.Models;

public class Department : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
