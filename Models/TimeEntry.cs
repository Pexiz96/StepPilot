using StepPilot.Data;

namespace StepPilot.Models;

public enum TimeEntryStatus
{
    Running,
    Submitted,
    Approved,
    Rejected
}

public class TimeEntry : TenantEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public DateTime ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public int BreakMinutes { get; set; }
    public TimeEntryStatus Status { get; set; } = TimeEntryStatus.Running;

    public bool IsManualCorrection { get; set; }
    public string? CorrectionReason { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime StartedAtLocal => ClockInUtc.ToLocalTime();
    public DateTime? EndedAtLocal => ClockOutUtc?.ToLocalTime();

    public int WorkedMinutes => ClockOutUtc is null
        ? 0
        : Math.Max(0, (int)Math.Round((ClockOutUtc.Value - ClockInUtc).TotalMinutes) - BreakMinutes);

    public decimal WorkedHours => WorkedMinutes / 60m;

    public int PlannedMinutes
    {
        get
        {
            if (Shift is null) return 0;
            var start = Shift.Date.ToDateTime(Shift.StartTime);
            var end = Shift.Date.ToDateTime(Shift.EndTime);
            if (end <= start) end = end.AddDays(1);
            return Math.Max(0, (int)Math.Round((end - start).TotalMinutes) - Shift.BreakMinutes);
        }
    }

    public int VarianceMinutes => WorkedMinutes - PlannedMinutes;
}