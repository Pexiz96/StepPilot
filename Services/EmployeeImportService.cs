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
    private const int QueryBatchSize = 1000;

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

        // Spaltenindizes nur einmal auflösen. Bei großen Dateien spart das tausende lineare Suchen.
        var indexes = BuildColumnIndexes(table, mapping);
        var importedNumbers = table.Rows
            .Select(row => Value(row, indexes, "EmployeeNumber").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // IN-Abfragen werden absichtlich in Blöcke geteilt, damit auch große Importe nicht am
        // SQL-Server-Parameterlimit scheitern.
        var existingNumbers = await LoadExistingEmployeeNumbersAsync(db, companyId, importedNumbers);

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<EmployeeImportPreviewRow>(table.Rows.Count);

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var source = table.Rows[i];
            var errors = new List<string>();
            var warnings = new List<string>();

            var employeeNumber = Value(source, indexes, "EmployeeNumber").Trim();
            var firstName = Value(source, indexes, "FirstName").Trim();
            var lastName = Value(source, indexes, "LastName").Trim();
            var email = NullIfBlank(Value(source, indexes, "Email"));
            var phone = NullIfBlank(Value(source, indexes, "PhoneNumber"));
            var position = Value(source, indexes, "Position").Trim();
            var location = NullIfBlank(Value(source, indexes, "Location"));
            var department = NullIfBlank(Value(source, indexes, "Department"));
            var qualifications = SplitQualifications(Value(source, indexes, "Qualifications"));

            if (string.IsNullOrWhiteSpace(employeeNumber)) errors.Add("Personalnummer fehlt.");
            if (string.IsNullOrWhiteSpace(firstName)) errors.Add("Vorname fehlt.");
            if (string.IsNullOrWhiteSpace(lastName)) errors.Add("Nachname fehlt.");
            if (!string.IsNullOrWhiteSpace(email) && !LooksLikeEmail(email)) errors.Add("E-Mail-Adresse ist ungültig.");

            decimal weeklyHours = 0m;
            var weeklyHoursText = Value(source, indexes, "WeeklyHours").Trim();
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
                i + 2, employeeNumber, firstName, lastName, email, phone, weeklyHours, position,
                location, department, qualifications, exists, errors, warnings));
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

        var validRows = preview.Rows.Where(x => x.IsValid).ToList();
        var failed = preview.Rows.Count - validRows.Count;
        var messages = preview.Rows
            .Where(x => !x.IsValid)
            .Select(x => $"Zeile {x.RowNumber}: nicht importiert – {string.Join(" ", x.Errors)}")
            .Take(500)
            .ToList();

        try
        {
            // Stammdaten einmal laden und fehlende Einträge gesammelt erzeugen.
            var locationList = await db.Locations.Where(x => x.CompanyId == companyId).ToListAsync();
            var departmentList = await db.Departments.Where(x => x.CompanyId == companyId).ToListAsync();
            var qualificationList = await db.Qualifications.Where(x => x.CompanyId == companyId).ToListAsync();

            var locations = ToNameDictionary(locationList, x => x.Name);
            var departments = ToNameDictionary(departmentList, x => x.Name);
            var qualifications = ToNameDictionary(qualificationList, x => x.Name);

            if (createMissingMasterData)
            {
                foreach (var name in validRows.Select(x => x.Location).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (locations.ContainsKey(name)) continue;
                    var entity = new Location { CompanyId = companyId, Name = name, IsActive = true };
                    db.Locations.Add(entity);
                    locations[name] = entity;
                }

                foreach (var name in validRows.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (departments.ContainsKey(name)) continue;
                    var entity = new Department { CompanyId = companyId, Name = name, IsActive = true };
                    db.Departments.Add(entity);
                    departments[name] = entity;
                }

                foreach (var name in validRows.SelectMany(x => x.Qualifications).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (qualifications.ContainsKey(name)) continue;
                    var entity = new Qualification { CompanyId = companyId, Name = name, IsActive = true };
                    db.Qualifications.Add(entity);
                    qualifications[name] = entity;
                }

                // Ein SaveChanges für alle neuen Stammdaten, damit ihre IDs für Mitarbeiter verfügbar sind.
                await db.SaveChangesAsync();
            }

            var employeeNumbers = validRows.Select(x => x.EmployeeNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var employeeList = await LoadEmployeesForImportAsync(db, companyId, employeeNumbers);
            var employees = employeeList.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);

            var created = 0;
            var updated = 0;
            var skipped = 0;
            var processedEmployees = new List<(Employee Employee, EmployeeImportPreviewRow Row, bool Existing)>();

            foreach (var row in validRows)
            {
                var isExisting = employees.TryGetValue(row.EmployeeNumber, out var employee);
                if (isExisting && duplicateMode == ImportDuplicateMode.Skip)
                {
                    skipped++;
                    continue;
                }

                if (!isExisting)
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
                else
                {
                    updated++;
                }

                employee!.FirstName = row.FirstName;
                employee.LastName = row.LastName;
                employee.Email = row.Email;
                employee.PhoneNumber = row.PhoneNumber;
                employee.WeeklyHours = row.WeeklyHours;
                employee.Position = row.Position;

                if (!string.IsNullOrWhiteSpace(row.Location))
                {
                    if (locations.TryGetValue(row.Location, out var location))
                        employee.LocationId = location.Id;
                    else
                        AddLimitedMessage(messages, $"Zeile {row.RowNumber}: Standort „{row.Location}“ existiert nicht und wurde nicht angelegt.");
                }

                if (!string.IsNullOrWhiteSpace(row.Department))
                {
                    if (departments.TryGetValue(row.Department, out var department))
                        employee.DepartmentId = department.Id;
                    else
                        AddLimitedMessage(messages, $"Zeile {row.RowNumber}: Abteilung „{row.Department}“ existiert nicht und wurde nicht angelegt.");
                }

                processedEmployees.Add((employee, row, isExisting));
            }

            // Neue Mitarbeiter bekommen hier gesammelt ihre IDs; Updates werden ebenfalls in einem Rutsch gespeichert.
            await db.SaveChangesAsync();

            if (duplicateMode == ImportDuplicateMode.Update)
            {
                var qualificationsToRemove = processedEmployees
                    .Where(x => x.Existing && x.Employee.Qualifications.Count > 0)
                    .SelectMany(x => x.Employee.Qualifications)
                    .ToList();

                if (qualificationsToRemove.Count > 0)
                    db.EmployeeQualifications.RemoveRange(qualificationsToRemove);
            }

            // Qualifikationsbeziehungen gesammelt aufbauen. Kein SaveChanges pro Mitarbeiter/Qualifikation.
            foreach (var item in processedEmployees)
            {
                var desiredIds = new HashSet<int>();
                foreach (var qualificationName in item.Row.Qualifications)
                {
                    if (!qualifications.TryGetValue(qualificationName, out var qualification))
                    {
                        AddLimitedMessage(messages, $"Zeile {item.Row.RowNumber}: Qualifikation „{qualificationName}“ existiert nicht und wurde nicht angelegt.");
                        continue;
                    }

                    desiredIds.Add(qualification.Id);
                }

                var existingIds = duplicateMode == ImportDuplicateMode.Update
                    ? new HashSet<int>()
                    : item.Employee.Qualifications.Select(x => x.QualificationId).ToHashSet();

                foreach (var qualificationId in desiredIds.Where(id => !existingIds.Contains(id)))
                {
                    db.EmployeeQualifications.Add(new EmployeeQualification
                    {
                        EmployeeId = item.Employee.Id,
                        QualificationId = qualificationId
                    });
                }
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            if (messages.Count >= 500)
                messages.Add("Weitere Hinweise wurden aus Performancegründen nicht einzeln protokolliert.");

            return new EmployeeImportResult(created, updated, skipped, failed, messages);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static Dictionary<string, int> BuildColumnIndexes(
        ImportTable table,
        IReadOnlyDictionary<string, string?> mapping)
    {
        var headerIndexes = table.Headers
            .Select((header, index) => new { header, index })
            .ToDictionary(x => x.header, x => x.index, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapping)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && headerIndexes.TryGetValue(pair.Value, out var index))
                result[pair.Key] = index;
        }
        return result;
    }

    private static string Value(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> indexes, string key)
    {
        return indexes.TryGetValue(key, out var index) && index >= 0 && index < row.Count
            ? row[index] ?? string.Empty
            : string.Empty;
    }

    private static async Task<HashSet<string>> LoadExistingEmployeeNumbersAsync(
        ApplicationDbContext db,
        int companyId,
        IReadOnlyList<string> employeeNumbers)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in employeeNumbers.Chunk(QueryBatchSize))
        {
            var numbers = batch.ToList();
            var found = await db.Employees.AsNoTracking()
                .Where(x => x.CompanyId == companyId && numbers.Contains(x.EmployeeNumber))
                .Select(x => x.EmployeeNumber)
                .ToListAsync();
            result.UnionWith(found);
        }
        return result;
    }

    private static async Task<List<Employee>> LoadEmployeesForImportAsync(
        ApplicationDbContext db,
        int companyId,
        IReadOnlyList<string> employeeNumbers)
    {
        var result = new List<Employee>();
        foreach (var batch in employeeNumbers.Chunk(QueryBatchSize))
        {
            var numbers = batch.ToList();
            var found = await db.Employees
                .Include(x => x.Qualifications)
                .Where(x => x.CompanyId == companyId && numbers.Contains(x.EmployeeNumber))
                .ToListAsync();
            result.AddRange(found);
        }
        return result;
    }

    private static Dictionary<string, T> ToNameDictionary<T>(IEnumerable<T> items, Func<T, string> selector) where T : class
    {
        return items
            .Where(x => !string.IsNullOrWhiteSpace(selector(x)))
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static void AddLimitedMessage(List<string> messages, string message)
    {
        if (messages.Count < 500)
            messages.Add(message);
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
