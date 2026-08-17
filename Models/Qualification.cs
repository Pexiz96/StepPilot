namespace StepPilot.Models;

public class Qualification : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<EmployeeQualification> Employees { get; set; } = new List<EmployeeQualification>();
}
