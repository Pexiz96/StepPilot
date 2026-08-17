namespace StepPilot.Models;

public enum AbsenceType
{
    Vacation = 0,
    Sick = 1,
    Training = 2,
    SpecialLeave = 3,
    Other = 4
}

public enum AbsenceStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2
}

public class Absence : TenantEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public AbsenceType Type { get; set; }
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Approved;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
