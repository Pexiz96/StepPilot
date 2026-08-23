namespace StepPilot.Models;

public class ComplianceProfile : TenantEntity
{
    public string Name { get; set; } = "Deutschland – Standardprofil";

    public bool IsActive { get; set; } = true;

    public decimal MinimumRestHours { get; set; } = 11m;

    public decimal StandardDailyHours { get; set; } = 8m;

    public decimal MaximumDailyHours { get; set; } = 10m;

    public int MinimumBreakAfter6HoursMinutes { get; set; } = 30;

    public int MinimumBreakAfter9HoursMinutes { get; set; } = 45;

    public int MinimumFreeSundaysPerYear { get; set; } = 15;

    public bool SundayWorkExceptionConfigured { get; set; }

    public bool HolidayWorkExceptionConfigured { get; set; }

    public string? LegalBasisNote { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
