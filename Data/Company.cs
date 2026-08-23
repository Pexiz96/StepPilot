namespace StepPilot.Data;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 0 = keine automatische Löschung. Fristen müssen vom Betreiber anhand
    // der konkreten Rechtsgrundlage und Aufbewahrungspflichten festgelegt werden.
    public int TimeEntryRetentionMonths { get; set; }
    public int AuditLogRetentionMonths { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
