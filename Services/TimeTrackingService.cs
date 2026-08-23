using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public sealed record TimeTrackingSummary(
    int PlannedMinutes,
    int WorkedMinutes,
    int DifferenceMinutes,
    int ApprovedMinutes,
    int OpenEntries,
    int ComplianceWarnings);

public sealed record TimeComplianceFinding(int TimeEntryId, string Severity, string Message, string Reference);

public class TimeTrackingService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    CurrentUserService currentUserService,
    TenantGuard tenantGuard)
{
    public async Task<int?> GetCurrentEmployeeIdAsync()
    {
        var user = await currentUserService.GetUserAsync();
        if (user?.CompanyId is null) return null;

        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == user.CompanyId && x.ApplicationUserId == user.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<TimeEntry?> GetActiveAsync(int employeeId)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TimeEntries.AsNoTracking()
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null);
    }

    public Task<TimeEntry?> GetRunningAsync(int companyId, int employeeId) => GetRunningInternalAsync(companyId, employeeId);

    private async Task<TimeEntry?> GetRunningInternalAsync(int companyId, int employeeId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TimeEntries.AsNoTracking().Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null);
    }

    public async Task<TimeEntry> ClockInAsync(int employeeId)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();

        var employee = await db.Employees.AsNoTracking().FirstAsync(x => x.CompanyId == companyId && x.Id == employeeId && x.IsActive);
        if (await db.TimeEntries.AnyAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null))
            throw new InvalidOperationException("Es läuft bereits eine Zeiterfassung.");

        var nowLocal = DateTime.Now;
        var today = DateOnly.FromDateTime(nowLocal);
        var assignment = await db.ShiftAssignments.AsNoTracking()
            .Include(x => x.Shift)
            .Where(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.Shift != null && x.Shift.Date == today)
            .OrderBy(x => x.Shift!.StartTime)
            .FirstOrDefaultAsync();

        var shift = assignment?.Shift;
        var entry = new TimeEntry
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            ShiftId = shift?.Id,
            LocationId = shift?.LocationId ?? employee.LocationId,
            DepartmentId = shift?.DepartmentId ?? employee.DepartmentId,
            ClockInUtc = DateTime.UtcNow,
            Status = TimeEntryStatus.Running
        };

        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
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

    public async Task ClockOutAsync(int entryId, int breakMinutes)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == entryId && x.ClockOutUtc == null)
            ?? throw new InvalidOperationException("Keine laufende Zeiterfassung gefunden.");
        CompleteEntry(entry, breakMinutes);
        await db.SaveChangesAsync();
    }

    public async Task ClockOutAsync(int companyId, int employeeId, int breakMinutes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null)
            ?? throw new InvalidOperationException("Keine laufende Zeiterfassung gefunden.");
        CompleteEntry(entry, breakMinutes);
        await db.SaveChangesAsync();
    }

    private static void CompleteEntry(TimeEntry entry, int breakMinutes)
    {
        entry.ClockOutUtc = DateTime.UtcNow;
        entry.BreakMinutes = Math.Max(0, breakMinutes);
        entry.Status = TimeEntryStatus.Submitted;
        entry.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task<List<TimeEntry>> GetForEmployeeAsync(int employeeId, DateOnly from, DateOnly toInclusive)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var toUtc = toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        return await GetEntriesAsync(companyId, fromUtc, toUtc, employeeId);
    }

    public async Task<List<TimeEntry>> GetCompanyEntriesAsync(int companyId, DateOnly from, DateOnly toInclusive)
    {
        var allowedCompanyId = await tenantGuard.RequireCompanyIdAsync();
        if (allowedCompanyId != companyId) throw new UnauthorizedAccessException();
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var toUtc = toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        return await GetEntriesAsync(companyId, fromUtc, toUtc);
    }

    public async Task<List<TimeEntry>> GetEntriesAsync(int companyId, DateTime fromUtc, DateTime toUtc, int? employeeId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.TimeEntries.AsNoTracking()
            .Include(x => x.Employee).Include(x => x.Location).Include(x => x.Department).Include(x => x.Shift)
            .Where(x => x.CompanyId == companyId && x.ClockInUtc >= fromUtc && x.ClockInUtc < toUtc);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        return await query.OrderByDescending(x => x.ClockInUtc).ToListAsync();
    }

    public async Task<TimeTrackingSummary> GetSummaryAsync(int employeeId, DateOnly from, DateOnly toInclusive)
    {
        var entries = await GetForEmployeeAsync(employeeId, from, toInclusive);
        var findings = await EvaluateActualComplianceAsync(employeeId, from, toInclusive);
        var planned = entries.Where(x => x.ClockOutUtc is not null).Sum(x => x.PlannedMinutes);
        var worked = entries.Where(x => x.ClockOutUtc is not null).Sum(x => x.WorkedMinutes);
        var approved = entries.Where(x => x.Status == TimeEntryStatus.Approved).Sum(x => x.WorkedMinutes);
        return new TimeTrackingSummary(planned, worked, worked - planned, approved, entries.Count(x => x.Status is TimeEntryStatus.Running or TimeEntryStatus.Submitted), findings.Count);
    }

    public async Task<List<TimeComplianceFinding>> EvaluateActualComplianceAsync(int employeeId, DateOnly from, DateOnly toInclusive)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        var entries = await GetForEmployeeAsync(employeeId, from, toInclusive);
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.ComplianceProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.IsActive)
            ?? new ComplianceProfile { CompanyId = companyId };

        var result = new List<TimeComplianceFinding>();
        var completed = entries.Where(x => x.ClockOutUtc is not null).OrderBy(x => x.ClockInUtc).ToList();

        foreach (var entry in completed)
        {
            var grossMinutes = (int)Math.Round((entry.ClockOutUtc!.Value - entry.ClockInUtc).TotalMinutes);
            if (entry.WorkedHours > profile.MaximumDailyHours)
                result.Add(new(entry.Id, "Fehler", $"Tatsächliche Arbeitszeit beträgt {entry.WorkedHours:0.##} Std. und überschreitet die konfigurierte Tageshöchstgrenze von {profile.MaximumDailyHours:0.##} Std.", "ArbZG § 3 / Regelprofil"));
            else if (entry.WorkedHours > profile.StandardDailyHours)
                result.Add(new(entry.Id, "Warnung", $"Tatsächliche Arbeitszeit liegt mit {entry.WorkedHours:0.##} Std. über der Standard-Tagesarbeitszeit von {profile.StandardDailyHours:0.##} Std.", "ArbZG § 3 / Ausgleich prüfen"));

            var requiredBreak = grossMinutes > 9 * 60 ? profile.MinimumBreakAfter9HoursMinutes : grossMinutes > 6 * 60 ? profile.MinimumBreakAfter6HoursMinutes : 0;
            if (entry.BreakMinutes < requiredBreak)
                result.Add(new(entry.Id, "Fehler", $"Erfasste Pause: {entry.BreakMinutes} Min.; erforderlich nach Regelprofil: mindestens {requiredBreak} Min.", "ArbZG § 4 / Regelprofil"));
        }

        for (var i = 1; i < completed.Count; i++)
        {
            var rest = completed[i].ClockInUtc - completed[i - 1].ClockOutUtc!.Value;
            if ((decimal)rest.TotalHours < profile.MinimumRestHours)
                result.Add(new(completed[i].Id, "Fehler", $"Zwischen zwei erfassten Arbeitseinsätzen liegen nur {rest.TotalHours:0.##} Std. Ruhezeit; Regelprofil verlangt {profile.MinimumRestHours:0.##} Std.", "ArbZG § 5 / Regelprofil"));
        }

        return result;
    }

    public async Task ApproveAsync(int id, string? userId)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await ApproveAsync(companyId, id, userId ?? string.Empty);
    }

    public async Task ApproveAsync(int companyId, int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        if (entry.ClockOutUtc is null) throw new InvalidOperationException("Laufende Zeiterfassungen können nicht freigegeben werden.");
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