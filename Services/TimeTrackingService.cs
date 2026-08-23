using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public class TimeTrackingService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<TimeEntry?> GetRunningAsync(int companyId, int employeeId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TimeEntries.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null);
    }

    public async Task<TimeEntry> ClockInAsync(int companyId, int employeeId, int? shiftId = null, int? locationId = null, int? departmentId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.TimeEntries.AnyAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null))
            throw new InvalidOperationException("Es läuft bereits eine Zeiterfassung.");

        var entry = new TimeEntry { CompanyId = companyId, EmployeeId = employeeId, ShiftId = shiftId, LocationId = locationId, DepartmentId = departmentId, ClockInUtc = DateTime.UtcNow };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task ClockOutAsync(int companyId, int employeeId, int breakMinutes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null)
            ?? throw new InvalidOperationException("Keine laufende Zeiterfassung gefunden.");
        entry.ClockOutUtc = DateTime.UtcNow;
        entry.BreakMinutes = Math.Max(0, breakMinutes);
        entry.Status = TimeEntryStatus.Submitted;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<TimeEntry>> GetEntriesAsync(int companyId, DateTime fromUtc, DateTime toUtc, int? employeeId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.TimeEntries.AsNoTracking().Include(x => x.Employee).Include(x => x.Location).Include(x => x.Department)
            .Where(x => x.CompanyId == companyId && x.ClockInUtc >= fromUtc && x.ClockInUtc < toUtc);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        return await query.OrderByDescending(x => x.ClockInUtc).ToListAsync();
    }

    public async Task ApproveAsync(int companyId, int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        entry.Status = TimeEntryStatus.Approved;
        entry.ApprovedByUserId = userId;
        entry.ApprovedAtUtc = DateTime.UtcNow;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CorrectAsync(int companyId, int id, DateTime clockInUtc, DateTime clockOutUtc, int breakMinutes, string reason)
    {
        if (clockOutUtc <= clockInUtc) throw new InvalidOperationException("Arbeitsende muss nach Arbeitsbeginn liegen.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Für Korrekturen ist eine Begründung erforderlich.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        entry.ClockInUtc = clockInUtc;
        entry.ClockOutUtc = clockOutUtc;
        entry.BreakMinutes = Math.Max(0, breakMinutes);
        entry.IsManualCorrection = true;
        entry.CorrectionReason = reason.Trim();
        entry.Status = TimeEntryStatus.Submitted;
        entry.ApprovedByUserId = null;
        entry.ApprovedAtUtc = null;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}