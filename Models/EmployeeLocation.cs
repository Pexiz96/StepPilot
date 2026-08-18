namespace StepPilot.Models;

public class EmployeeLocation
{
    public int CompanyId { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }
}
