using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed class EmployeeImportHistoryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public EmployeeImportHistoryService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task RecordAsync(
        string fileName,
        int totalRows,
        EmployeeImportResult result,
        ImportDuplicateMode duplicateMode,
        bool createMissingMasterData,
        DateTime startedAtUtc)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        db.EmployeeImportRuns.Add(new EmployeeImportRun
        {
            CompanyId = companyId,
            FileName = fileName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = DateTime.UtcNow,
            TotalRows = totalRows,
            Created = result.Created,
            Updated = result.Updated,
            Skipped = result.Skipped,
            Failed = result.Failed,
            DuplicateMode = duplicateMode.ToString(),
            CreatedMissingMasterData = createMissingMasterData,
            Summary = result.Messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, result.Messages.Take(100))
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<EmployeeImportRun>> GetRecentAsync(int take = 20)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        return await db.EmployeeImportRuns.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();
    }
}
