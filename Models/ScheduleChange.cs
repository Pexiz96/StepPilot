namespace StepPilot.Models;

public class ScheduleChange : TenantEntity
{
    public int ScheduleId { get; set; }
    public Schedule? Schedule { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
}
