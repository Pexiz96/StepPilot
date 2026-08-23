using StepPilot.Data;

namespace StepPilot.Models;

public class EmployeeImportRun : TenantEntity
{
    public string FileName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedAtUtc { get; set; } = DateTime.UtcNow;
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public string DuplicateMode { get; set; } = string.Empty;
    public bool CreatedMissingMasterData { get; set; }
    public string? Summary { get; set; }
}
