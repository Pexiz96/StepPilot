namespace StepPilot.Models;

public class TimeEntryRevision : TenantEntity
{
    public int TimeEntryId { get; set; }
    public TimeEntry? TimeEntry { get; set; }

    public DateTime PreviousClockInUtc { get; set; }
    public DateTime? PreviousClockOutUtc { get; set; }
    public int PreviousBreakMinutes { get; set; }
    public TimeEntryStatus PreviousStatus { get; set; }

    public DateTime NewClockInUtc { get; set; }
    public DateTime? NewClockOutUtc { get; set; }
    public int NewBreakMinutes { get; set; }
    public TimeEntryStatus NewStatus { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
