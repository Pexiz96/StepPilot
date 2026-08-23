namespace StepPilot.Models;

public class AuditLogEntry : TenantEntity
{
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? UserId { get; set; }
}
