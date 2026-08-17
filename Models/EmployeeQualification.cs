namespace StepPilot.Models;

public class EmployeeQualification
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int QualificationId { get; set; }
    public Qualification? Qualification { get; set; }
}
