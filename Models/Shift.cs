namespace StepPilot.Models;

public class Shift : TenantEntity
{
    public int ScheduleId { get; set; }
    public Schedule? Schedule { get; set; }

    public int? ShiftTemplateId { get; set; }
    public ShiftTemplate? ShiftTemplate { get; set; }

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }

    public int RequiredEmployees { get; set; } = 1;

    public int? RequiredQualificationId { get; set; }
    public Qualification? RequiredQualification { get; set; }

    public string? Notes { get; set; }

    public ICollection<ShiftAssignment> Assignments { get; set; } = new List<ShiftAssignment>();
}
