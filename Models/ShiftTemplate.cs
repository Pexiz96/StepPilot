namespace StepPilot.Models;

public class ShiftTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}
