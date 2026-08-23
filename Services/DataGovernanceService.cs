using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed record RetentionPreview(
    int TimeEntriesEligible,
    int AuditEntriesEligible,
    DateTime? TimeEntryCutoffUtc,
    DateTime? AuditLogCutoffUtc);

public sealed record RetentionPurgeResult(int DeletedTimeEntries, int DeletedAuditEntries);

public sealed class DataGovernanceService(
    IDbContextFactory<ApplicationDbContext> factory,
    TenantGuard tenant,
    AuditLogService auditLog,
    CurrentUserService currentUser)
{
    public async Task<RetentionPreview> GetRetentionPreviewAsync()
    {
        var companyId = await tenant.RequireCompanyIdAsync();
        await using var db = await factory.CreateDbContextAsync();
        var company = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
        var now = DateTime.UtcNow;

        DateTime? timeCutoff = company.TimeEntryRetentionMonths > 0 ? now.AddMonths(-company.TimeEntryRetentionMonths) : null;
        DateTime? auditCutoff = company.AuditLogRetentionMonths > 0 ? now.AddMonths(-company.AuditLogRetentionMonths) : null;

        var timeCount = timeCutoff is null
            ? 0
            : await db.TimeEntries.CountAsync(x => x.CompanyId == companyId && x.ClockOutUtc != null && x.ClockOutUtc < timeCutoff.Value);
        var auditCount = auditCutoff is null
            ? 0
            : await db.AuditLogEntries.CountAsync(x => x.CompanyId == companyId && x.OccurredAtUtc < auditCutoff.Value);

        return new RetentionPreview(timeCount, auditCount, timeCutoff, auditCutoff);
    }

    public async Task<RetentionPurgeResult> PurgeExpiredOperationalDataAsync(string confirmation)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
            throw new InvalidOperationException("Löschung nicht bestätigt. Gib exakt DELETE als Bestätigung an.");

        var companyId = await tenant.RequireCompanyIdAsync();
        await using var db = await factory.CreateDbContextAsync();
        var company = await db.Companies.FirstAsync(x => x.Id == companyId);
        var now = DateTime.UtcNow;
        var deletedTimeEntries = 0;
        var deletedAuditEntries = 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        if (company.TimeEntryRetentionMonths > 0)
        {
            var cutoff = now.AddMonths(-company.TimeEntryRetentionMonths);
            var entries = await db.TimeEntries
                .Where(x => x.CompanyId == companyId && x.ClockOutUtc != null && x.ClockOutUtc < cutoff)
                .ToListAsync();
            deletedTimeEntries = entries.Count;
            db.TimeEntries.RemoveRange(entries);
        }

        if (company.AuditLogRetentionMonths > 0)
        {
            var cutoff = now.AddMonths(-company.AuditLogRetentionMonths);
            var logs = await db.AuditLogEntries
                .Where(x => x.CompanyId == companyId && x.OccurredAtUtc < cutoff)
                .ToListAsync();
            deletedAuditEntries = logs.Count;
            db.AuditLogEntries.RemoveRange(logs);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await auditLog.WriteAsync(
            "DataGovernance",
            "RetentionPurge",
            "Company",
            companyId.ToString(),
            $"Aufbewahrungsbereinigung ausgeführt: {deletedTimeEntries} Zeitbuchungen, {deletedAuditEntries} Audit-Einträge gelöscht.",
            (await currentUser.GetUserAsync())?.Id);

        return new RetentionPurgeResult(deletedTimeEntries, deletedAuditEntries);
    }

    public async Task<string> ExportEmployeeDataAsync(int employeeId)
    {
        var companyId = await tenant.RequireCompanyIdAsync();
        await using var db = await factory.CreateDbContextAsync();

        var employee = await db.Employees.AsNoTracking()
            .Include(x => x.Location)
            .Include(x => x.Department)
            .Include(x => x.AdditionalLocations).ThenInclude(x => x.Location)
            .Include(x => x.Qualifications).ThenInclude(x => x.Qualification)
            .FirstAsync(x => x.CompanyId == companyId && x.Id == employeeId);

        var absences = await db.Absences.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.EmployeeId == employeeId)
            .OrderBy(x => x.StartDate)
            .ToListAsync();

        var timeEntryEntities = await db.TimeEntries.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.EmployeeId == employeeId)
            .OrderBy(x => x.ClockInUtc)
            .ToListAsync();

        var assignmentEntities = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .Where(x => x.CompanyId == companyId && x.EmployeeId == employeeId)
            .OrderBy(x => x.Shift!.Date)
            .ToListAsync();

        var payload = new
        {
            ExportedAtUtc = DateTime.UtcNow,
            Employee = new
            {
                employee.Id,
                employee.EmployeeNumber,
                employee.FirstName,
                employee.LastName,
                employee.Email,
                employee.PhoneNumber,
                employee.WeeklyHours,
                employee.HireDate,
                employee.LeaveDate,
                employee.Position,
                employee.IsActive,
                MainLocation = employee.Location?.Name,
                Department = employee.Department?.Name,
                AdditionalLocations = employee.AdditionalLocations.Select(x => x.Location?.Name).Where(x => x is not null).ToArray(),
                Qualifications = employee.Qualifications.Select(x => x.Qualification?.Name).Where(x => x is not null).ToArray()
            },
            Absences = absences.Select(x => new { x.Id, x.StartDate, x.EndDate, Type = x.Type.ToString(), Status = x.Status.ToString(), x.Reason }).ToArray(),
            TimeEntries = timeEntryEntities.Select(x => new
            {
                x.Id,
                x.ClockInUtc,
                x.ClockOutUtc,
                x.BreakMinutes,
                Status = x.Status.ToString(),
                x.IsManualCorrection,
                x.CorrectionReason,
                x.RejectionReason,
                x.LocationId,
                x.DepartmentId,
                x.ShiftId
            }).ToArray(),
            ShiftAssignments = assignmentEntities.Select(x => new
            {
                x.Id,
                x.ShiftId,
                Date = x.Shift?.Date,
                StartTime = x.Shift?.StartTime,
                EndTime = x.Shift?.EndTime
            }).ToArray()
        };

        await auditLog.WriteAsync(
            "DataGovernance",
            "EmployeeExport",
            nameof(Employee),
            employeeId.ToString(),
            "Datenauskunft für Mitarbeiter exportiert.",
            (await currentUser.GetUserAsync())?.Id);

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
