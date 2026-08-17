namespace StepPilot.Models;

public class Availability : TenantEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly? AvailableFrom { get; set; }
    public TimeOnly? AvailableUntil { get; set; }
    public bool IsAvailable { get; set; } = true;
}
