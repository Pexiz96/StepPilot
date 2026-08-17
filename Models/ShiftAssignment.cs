namespace StepPilot.Models;

public class ShiftAssignment
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
