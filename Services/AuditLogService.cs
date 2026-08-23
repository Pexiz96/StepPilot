using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class AuditLogService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public AuditLogService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task WriteAsync(string category, string action, string entityType, string? entityId, string summary, string? userId = null)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            CompanyId = companyId,
            OccurredAtUtc = DateTime.UtcNow,
            Category = category,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            UserId = userId
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLogEntry>> GetRecentAsync(int take = 250)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync();
    }
}
