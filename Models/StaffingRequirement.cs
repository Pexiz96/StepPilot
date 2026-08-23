namespace StepPilot.Models;

public class StaffingRequirement : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public int RequiredEmployees { get; set; } = 1;
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? RequiredQualificationId { get; set; }
    public Qualification? RequiredQualification { get; set; }
    public bool IsActive { get; set; } = true;
}
