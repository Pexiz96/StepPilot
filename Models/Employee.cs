using StepPilot.Data;

namespace StepPilot.Models;

public class Employee : TenantEntity
{
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public decimal WeeklyHours { get; set; }

    public DateOnly HireDate { get; set; }
        = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? LeaveDate { get; set; }

    public string Position { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int? LocationId { get; set; }

    public Location? Location { get; set; }

    public int? DepartmentId { get; set; }

    public Department? Department { get; set; }

    public string? ApplicationUserId { get; set; }

    public ApplicationUser? ApplicationUser { get; set; }

    public ICollection<EmployeeQualification> Qualifications { get; set; }
        = new List<EmployeeQualification>();

    public ICollection<Availability> Availabilities { get; set; }
        = new List<Availability>();

    public ICollection<EmployeeShiftPreference> ShiftPreferences { get; set; }
        = new List<EmployeeShiftPreference>();

    public ICollection<ShiftAssignment> ShiftAssignments { get; set; }
        = new List<ShiftAssignment>();

    public ICollection<Absence> Absences { get; set; }
        = new List<Absence>();
}