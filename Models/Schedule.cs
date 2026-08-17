namespace StepPilot.Models;

public enum ScheduleStatus
{
    Draft = 0,
    Published = 1,
    Changed = 2,
    Closed = 3
}

public class Schedule : TenantEntity
{
    public DateOnly WeekStart { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
}
