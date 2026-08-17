namespace StepPilot.Models;

public enum ShiftSwapRequestStatus
{
    Open = 0,
    Claimed = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

public class ShiftSwapRequest
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    public int ShiftAssignmentId { get; set; }
    public ShiftAssignment? ShiftAssignment { get; set; }

    public int FromEmployeeId { get; set; }
    public Employee? FromEmployee { get; set; }

    public int? ToEmployeeId { get; set; }
    public Employee? ToEmployee { get; set; }

    public ShiftSwapRequestStatus Status { get; set; } = ShiftSwapRequestStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
}
