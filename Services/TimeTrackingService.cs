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
    TenantGuard tenantGuard,
    AuditLogService auditLogService)
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

    public async Task<TimeEntry?> GetRunningAsync(int companyId, int employeeId)
    {
        await EnsureCompanyAccessAsync(companyId);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TimeEntries.AsNoTracking().Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null);
    }

    public async Task<TimeEntry> ClockInAsync(int employeeId)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();

        var employee = await db.Employees.AsNoTracking()
            .Include(x => x.Location)
            .FirstAsync(x => x.CompanyId == companyId && x.Id == employeeId && x.IsActive);

        if (await db.TimeEntries.AnyAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null))
            throw new InvalidOperationException("Es läuft bereits eine Zeiterfassung.");

        var nowUtc = DateTime.UtcNow;
        var localNow = ConvertUtcToZone(nowUtc, employee.Location?.TimeZoneId);
        var today = DateOnly.FromDateTime(localNow);

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
            ClockInUtc = nowUtc,
            Status = TimeEntryStatus.Running
        };

        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "ClockIn", nameof(TimeEntry), entry.Id.ToString(), $"Arbeitszeit gestartet; MitarbeiterId={employeeId}.", (await currentUserService.GetUserAsync())?.Id);
        return entry;
    }

    public async Task<TimeEntry> ClockInAsync(int companyId, int employeeId, int? shiftId = null, int? locationId = null, int? departmentId = null)
    {
        await EnsureCompanyAccessAsync(companyId);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Employees.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == employeeId && x.IsActive))
            throw new InvalidOperationException("Mitarbeiter wurde nicht gefunden oder ist inaktiv.");
        if (await db.TimeEntries.AnyAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null))
            throw new InvalidOperationException("Es läuft bereits eine Zeiterfassung.");

        var entry = new TimeEntry
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            ShiftId = shiftId,
            LocationId = locationId,
            DepartmentId = departmentId,
            ClockInUtc = DateTime.UtcNow,
            Status = TimeEntryStatus.Running
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "ClockIn", nameof(TimeEntry), entry.Id.ToString(), $"Arbeitszeit gestartet; MitarbeiterId={employeeId}.", (await currentUserService.GetUserAsync())?.Id);
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
        await auditLogService.WriteAsync("TimeTracking", "ClockOut", nameof(TimeEntry), entry.Id.ToString(), $"Arbeitszeit beendet; Pause={entry.BreakMinutes} Min.", (await currentUserService.GetUserAsync())?.Id);
    }

    public async Task ClockOutAsync(int companyId, int employeeId, int breakMinutes)
    {
        await EnsureCompanyAccessAsync(companyId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmployeeId == employeeId && x.ClockOutUtc == null)
            ?? throw new InvalidOperationException("Keine laufende Zeiterfassung gefunden.");
        CompleteEntry(entry, breakMinutes);
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "ClockOut", nameof(TimeEntry), entry.Id.ToString(), $"Arbeitszeit beendet; Pause={entry.BreakMinutes} Min.", (await currentUserService.GetUserAsync())?.Id);
    }

    private static void CompleteEntry(TimeEntry entry, int breakMinutes)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc <= entry.ClockInUtc)
            throw new InvalidOperationException("Arbeitsende muss nach Arbeitsbeginn liegen.");

        var grossMinutes = (int)Math.Round((nowUtc - entry.ClockInUtc).TotalMinutes);
        if (breakMinutes < 0 || breakMinutes >= grossMinutes)
            throw new InvalidOperationException("Die Pausenzeit muss kleiner als die gesamte Anwesenheitszeit sein.");

        entry.ClockOutUtc = nowUtc;
        entry.BreakMinutes = breakMinutes;
        entry.Status = TimeEntryStatus.Submitted;
        entry.RejectionReason = null;
        entry.UpdatedAtUtc = nowUtc;
    }

    public async Task<List<TimeEntry>> GetForEmployeeAsync(int employeeId, DateOnly from, DateOnly toInclusive)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var zoneId = await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == employeeId)
            .Select(x => x.Location != null ? x.Location.TimeZoneId : null)
            .FirstOrDefaultAsync();

        var fromUtc = ConvertLocalToUtc(from.ToDateTime(TimeOnly.MinValue), zoneId);
        var toUtc = ConvertLocalToUtc(toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), zoneId);
        return await GetEntriesInternalAsync(companyId, fromUtc, toUtc, employeeId);
    }

    public async Task<List<TimeEntry>> GetCompanyEntriesAsync(int companyId, DateOnly from, DateOnly toInclusive)
    {
        await EnsureCompanyAccessAsync(companyId);
        // Company views may span several location time zones. A one-day safety margin avoids boundary loss;
        // UI grouping still uses each entry's timestamp and configured location.
        var fromUtc = from.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = toInclusive.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await GetEntriesInternalAsync(companyId, fromUtc, toUtc);
    }

    public async Task<List<TimeEntry>> GetEntriesAsync(int companyId, DateTime fromUtc, DateTime toUtc, int? employeeId = null)
    {
        await EnsureCompanyAccessAsync(companyId);
        return await GetEntriesInternalAsync(companyId, fromUtc, toUtc, employeeId);
    }

    private async Task<List<TimeEntry>> GetEntriesInternalAsync(int companyId, DateTime fromUtc, DateTime toUtc, int? employeeId = null)
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
        return new TimeTrackingSummary(planned, worked, worked - planned, approved, entries.Count(x => x.Status is TimeEntryStatus.Running or TimeEntryStatus.Submitted or TimeEntryStatus.Rejected), findings.Count);
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
            var workedMinutes = TimeTrackingRules.CalculateWorkedMinutes(entry.ClockInUtc, entry.ClockOutUtc.Value, entry.BreakMinutes);
            var workedHours = workedMinutes / 60m;

            if (workedHours > profile.MaximumDailyHours)
                result.Add(new(entry.Id, "Fehler", $"Tatsächliche Arbeitszeit beträgt {workedHours:0.##} Std. und überschreitet die konfigurierte Tageshöchstgrenze von {profile.MaximumDailyHours:0.##} Std.", "ArbZG § 3 / Regelprofil"));
            else if (workedHours > profile.StandardDailyHours)
                result.Add(new(entry.Id, "Warnung", $"Tatsächliche Arbeitszeit liegt mit {workedHours:0.##} Std. über der Standard-Tagesarbeitszeit von {profile.StandardDailyHours:0.##} Std. Ausgleich bzw. Ausnahmeregelung prüfen.", "ArbZG § 3 / manuelle Prüfung"));

            var requiredBreak = TimeTrackingRules.RequiredBreakMinutes(grossMinutes, profile.MinimumBreakAfter6HoursMinutes, profile.MinimumBreakAfter9HoursMinutes);
            if (entry.BreakMinutes < requiredBreak)
                result.Add(new(entry.Id, "Fehler", $"Erfasste Pause: {entry.BreakMinutes} Min.; nach dem aktiven Regelprofil sind mindestens {requiredBreak} Min. vorgesehen.", "ArbZG § 4 / Regelprofil"));

            if (entry.Status == TimeEntryStatus.Rejected)
                result.Add(new(entry.Id, "Prüfung", "Die Zeitbuchung wurde abgelehnt und muss vor einer endgültigen Auswertung geklärt werden.", "Interner Freigabeprozess"));
        }

        for (var i = 1; i < completed.Count; i++)
        {
            var restHours = TimeTrackingRules.RestHours(completed[i - 1].ClockOutUtc!.Value, completed[i].ClockInUtc);
            if (restHours < profile.MinimumRestHours)
                result.Add(new(completed[i].Id, "Fehler", $"Zwischen zwei erfassten Arbeitseinsätzen liegen nur {restHours:0.##} Std. Ruhezeit; Regelprofil verlangt {profile.MinimumRestHours:0.##} Std.", "ArbZG § 5 / Regelprofil"));
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
        await EnsureCompanyAccessAsync(companyId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        if (entry.ClockOutUtc is null) throw new InvalidOperationException("Laufende Zeiterfassungen können nicht freigegeben werden.");
        if (entry.Status == TimeEntryStatus.Rejected) throw new InvalidOperationException("Abgelehnte Zeitbuchungen müssen zuerst erneut eingereicht werden.");
        entry.Status = TimeEntryStatus.Approved;
        entry.RejectionReason = null;
        entry.ApprovedByUserId = userId;
        entry.ApprovedAtUtc = DateTime.UtcNow;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "Approve", nameof(TimeEntry), entry.Id.ToString(), "Zeitbuchung freigegeben.", userId);
    }

    public async Task RejectAsync(int id, string reason, string? userId)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Für eine Ablehnung ist eine Begründung erforderlich.");
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        if (entry.ClockOutUtc is null) throw new InvalidOperationException("Laufende Zeiterfassungen können nicht abgelehnt werden.");
        if (entry.Status == TimeEntryStatus.Approved) throw new InvalidOperationException("Bereits freigegebene Zeitbuchungen müssen über eine dokumentierte Korrektur geändert werden.");

        entry.Status = TimeEntryStatus.Rejected;
        entry.RejectionReason = reason.Trim();
        entry.ApprovedByUserId = null;
        entry.ApprovedAtUtc = null;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "Reject", nameof(TimeEntry), entry.Id.ToString(), $"Zeitbuchung abgelehnt. Grund: {entry.RejectionReason}", userId);
    }

    public async Task ResubmitAsync(int id)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        var user = await currentUserService.GetUserAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);
        if (entry.Status != TimeEntryStatus.Rejected) throw new InvalidOperationException("Nur abgelehnte Zeitbuchungen können erneut eingereicht werden.");
        entry.Status = TimeEntryStatus.Submitted;
        entry.RejectionReason = null;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "Resubmit", nameof(TimeEntry), entry.Id.ToString(), "Abgelehnte Zeitbuchung erneut eingereicht.", user?.Id);
    }

    public async Task CorrectAsync(int companyId, int id, DateTime clockInUtc, DateTime clockOutUtc, int breakMinutes, string reason)
    {
        await EnsureCompanyAccessAsync(companyId);
        if (clockOutUtc <= clockInUtc) throw new InvalidOperationException("Arbeitsende muss nach Arbeitsbeginn liegen.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Für Korrekturen ist eine Begründung erforderlich.");
        var grossMinutes = (int)Math.Round((clockOutUtc - clockInUtc).TotalMinutes);
        if (breakMinutes < 0 || breakMinutes >= grossMinutes) throw new InvalidOperationException("Die Pausenzeit muss kleiner als die gesamte Anwesenheitszeit sein.");

        var user = await currentUserService.GetUserAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.TimeEntries.FirstAsync(x => x.CompanyId == companyId && x.Id == id);

        var revision = new TimeEntryRevision
        {
            CompanyId = companyId,
            TimeEntryId = entry.Id,
            PreviousClockInUtc = entry.ClockInUtc,
            PreviousClockOutUtc = entry.ClockOutUtc,
            PreviousBreakMinutes = entry.BreakMinutes,
            PreviousStatus = entry.Status,
            NewClockInUtc = clockInUtc,
            NewClockOutUtc = clockOutUtc,
            NewBreakMinutes = breakMinutes,
            NewStatus = TimeEntryStatus.Submitted,
            Reason = reason.Trim(),
            ChangedByUserId = user?.Id,
            ChangedAtUtc = DateTime.UtcNow
        };
        db.TimeEntryRevisions.Add(revision);

        entry.ClockInUtc = DateTime.SpecifyKind(clockInUtc, DateTimeKind.Utc);
        entry.ClockOutUtc = DateTime.SpecifyKind(clockOutUtc, DateTimeKind.Utc);
        entry.BreakMinutes = breakMinutes;
        entry.IsManualCorrection = true;
        entry.CorrectionReason = reason.Trim();
        entry.RejectionReason = null;
        entry.Status = TimeEntryStatus.Submitted;
        entry.ApprovedByUserId = null;
        entry.ApprovedAtUtc = null;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await auditLogService.WriteAsync("TimeTracking", "Correct", nameof(TimeEntry), entry.Id.ToString(), $"Zeitbuchung korrigiert; RevisionId={revision.Id}. Grund: {revision.Reason}", user?.Id);
    }

    public async Task<List<TimeEntryRevision>> GetRevisionsAsync(int entryId)
    {
        var companyId = await tenantGuard.RequireCompanyIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TimeEntryRevisions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TimeEntryId == entryId)
            .OrderByDescending(x => x.ChangedAtUtc)
            .ToListAsync();
    }

    private async Task EnsureCompanyAccessAsync(int companyId)
    {
        var allowedCompanyId = await tenantGuard.RequireCompanyIdAsync();
        if (allowedCompanyId != companyId) throw new UnauthorizedAccessException();
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) timeZoneId = "Europe/Berlin";
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static DateTime ConvertUtcToZone(DateTime utc, string? timeZoneId)
    {
        if (utc.Kind != DateTimeKind.Utc) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, ResolveTimeZone(timeZoneId));
    }

    private static DateTime ConvertLocalToUtc(DateTime local, string? timeZoneId)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, ResolveTimeZone(timeZoneId));
    }
}
