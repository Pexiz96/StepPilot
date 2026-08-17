namespace StepPilot.Models;

public class EmployeeShiftPreference
{
    public int EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public int ShiftTemplateId { get; set; }

    public ShiftTemplate? ShiftTemplate { get; set; }

    public bool IsAllowed { get; set; } = true;
}