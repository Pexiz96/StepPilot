namespace StepPilot.Models;

public enum OpenShiftRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class OpenShiftRequest
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public OpenShiftRequestStatus Status { get; set; } = OpenShiftRequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}
