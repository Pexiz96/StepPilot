using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public enum ImportDuplicateMode
{
    Skip,
    Update
}

public sealed record EmployeeImportPreviewRow(
    int RowNumber,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    decimal WeeklyHours,
    string Position,
    string? Location,
    string? Department,
    IReadOnlyList<string> Qualifications,
    bool ExistsInDatabase,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record EmployeeImportPreview(
    IReadOnlyList<EmployeeImportPreviewRow> Rows,
    int ValidCount,
    int ErrorCount,
    int ExistingCount);

public sealed record EmployeeImportResult(
    int Created,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Messages);

public sealed class EmployeeImportService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly TenantGuard _tenant;

    public EmployeeImportService(IDbContextFactory<ApplicationDbContext> factory, TenantGuard tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<EmployeeImportPreview> ValidateAsync(
        ImportTable table,
        IReadOnlyDictionary<string, string?> mapping)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var existingNumbers = await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.EmployeeNumber)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<EmployeeImportPreviewRow>();

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var source = table.Rows[i];
            var errors = new List<string>();
            var warnings = new List<string>();

            var employeeNumber = Value(table, source, mapping, "EmployeeNumber").Trim();
            var firstName = Value(table, source, mapping, "FirstName").Trim();
            var lastName = Value(table, source, mapping, "LastName").Trim();
            var email = NullIfBlank(Value(table, source, mapping, "Email"));
            var phone = NullIfBlank(Value(table, source, mapping, "PhoneNumber"));
            var position = Value(table, source, mapping, "Position").Trim();
            var location = NullIfBlank(Value(table, source, mapping, "Location"));
            var department = NullIfBlank(Value(table, source, mapping, "Department"));
            var qualifications = SplitQualifications(Value(table, source, mapping, "Qualifications"));

            if (string.IsNullOrWhiteSpace(employeeNumber)) errors.Add("Personalnummer fehlt.");
            if (string.IsNullOrWhiteSpace(firstName)) errors.Add("Vorname fehlt.");
            if (string.IsNullOrWhiteSpace(lastName)) errors.Add("Nachname fehlt.");
            if (!string.IsNullOrWhiteSpace(email) && !LooksLikeEmail(email)) errors.Add("E-Mail-Adresse ist ungültig.");

            decimal weeklyHours = 0m;
            var weeklyHoursText = Value(table, source, mapping, "WeeklyHours").Trim();
            if (!string.IsNullOrWhiteSpace(weeklyHoursText) && !TryParseDecimal(weeklyHoursText, out weeklyHours))
                errors.Add("Wochenstunden konnten nicht gelesen werden.");
            else if (weeklyHours < 0 || weeklyHours > 168)
                errors.Add("Wochenstunden müssen zwischen 0 und 168 liegen.");

            if (!string.IsNullOrWhiteSpace(employeeNumber) && !seenNumbers.Add(employeeNumber))
                errors.Add("Personalnummer kommt mehrfach in der Importdatei vor.");

            var exists = !string.IsNullOrWhiteSpace(employeeNumber) && existingNumbers.Contains(employeeNumber);
            if (exists) warnings.Add("Personalnummer existiert bereits in StepPilot.");
            if (string.IsNullOrWhiteSpace(location)) warnings.Add("Kein Standort zugeordnet.");

            rows.Add(new EmployeeImportPreviewRow(
                i + 2,
                employeeNumber,
                firstName,
                lastName,
                email,
                phone,
                weeklyHours,
                position,
                location,
                department,
                qualifications,
                exists,
                errors,
                warnings));
        }

        return new EmployeeImportPreview(
            rows,
            rows.Count(x => x.IsValid),
            rows.Count(x => !x.IsValid),
            rows.Count(x => x.ExistsInDatabase));
    }

    public async Task<EmployeeImportResult> ImportAsync(
        EmployeeImportPreview preview,
        ImportDuplicateMode duplicateMode,
        bool createMissingMasterData = true)
    {
        var companyId = await _tenant.RequireCompanyIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var locations = await db.Locations.Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var departments = await db.Departments.Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var qualifications = await db.Qualifications.Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var employees = await db.Employees
            .Include(x => x.Qualifications)
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var messages = new List<string>();

        foreach (var row in preview.Rows)
        {
            if (!row.IsValid)
            {
                failed++;
                messages.Add($"Zeile {row.RowNumber}: nicht importiert – {string.Join(" ", row.Errors)}");
                continue;
            }

            if (employees.TryGetValue(row.EmployeeNumber, out var employee))
            {
                if (duplicateMode == ImportDuplicateMode.Skip)
                {
                    skipped++;
                    continue;
                }
                updated++;
            }
            else
            {
                employee = new Employee
                {
                    CompanyId = companyId,
                    EmployeeNumber = row.EmployeeNumber,
                    HireDate = DateOnly.FromDateTime(DateTime.Today),
                    IsActive = true
                };
                db.Employees.Add(employee);
                employees[row.EmployeeNumber] = employee;
                created++;
            }

            employee.FirstName = row.FirstName;
            employee.LastName = row.LastName;
            employee.Email = row.Email;
            employee.PhoneNumber = row.PhoneNumber;
            employee.WeeklyHours = row.WeeklyHours;
            employee.Position = row.Position;

            if (!string.IsNullOrWhiteSpace(row.Location))
            {
                if (!locations.TryGetValue(row.Location, out var location) && createMissingMasterData)
                {
                    location = new Location { CompanyId = companyId, Name = row.Location, IsActive = true };
                    db.Locations.Add(location);
                    locations[row.Location] = location;
                }
                if (location is not null)
                {
                    await db.SaveChangesAsync();
                    employee.LocationId = location.Id;
                }
            }

            if (!string.IsNullOrWhiteSpace(row.Department))
            {
                if (!departments.TryGetValue(row.Department, out var department) && createMissingMasterData)
                {
                    department = new Department { CompanyId = companyId, Name = row.Department, IsActive = true };
                    db.Departments.Add(department);
                    departments[row.Department] = department;
                }
                if (department is not null)
                {
                    await db.SaveChangesAsync();
                    employee.DepartmentId = department.Id;
                }
            }

            await db.SaveChangesAsync();

            if (duplicateMode == ImportDuplicateMode.Update && employee.Qualifications.Count > 0)
                db.EmployeeQualifications.RemoveRange(employee.Qualifications);

            foreach (var qualificationName in row.Qualifications)
            {
                if (!qualifications.TryGetValue(qualificationName, out var qualification) && createMissingMasterData)
                {
                    qualification = new Qualification { CompanyId = companyId, Name = qualificationName, IsActive = true };
                    db.Qualifications.Add(qualification);
                    await db.SaveChangesAsync();
                    qualifications[qualificationName] = qualification;
                }

                if (qualification is not null && !employee.Qualifications.Any(x => x.QualificationId == qualification.Id))
                {
                    employee.Qualifications.Add(new EmployeeQualification
                    {
                        EmployeeId = employee.Id,
                        QualificationId = qualification.Id
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return new EmployeeImportResult(created, updated, skipped, failed, messages);
    }

    private static string Value(
        ImportTable table,
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, string?> mapping,
        string key)
    {
        if (!mapping.TryGetValue(key, out var header) || string.IsNullOrWhiteSpace(header)) return string.Empty;
        var index = table.Headers.ToList().FindIndex(x => string.Equals(x, header, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> SplitQualifications(string value) =>
        value.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool LooksLikeEmail(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address == value; }
        catch { return false; }
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = value.Replace(" Std.", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Stunden", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}
