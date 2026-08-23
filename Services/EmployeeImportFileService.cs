using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components.Forms;

namespace StepPilot.Services;

public sealed record ImportTable(
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed class EmployeeImportFileService
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    public async Task<ImportTable> ReadAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        await using var stream = file.OpenReadStream(MaxFileSize, cancellationToken);

        return extension switch
        {
            ".csv" or ".txt" => await ReadCsvAsync(file.Name, stream, cancellationToken),
            ".xlsx" => await ReadXlsxAsync(file.Name, stream, cancellationToken),
            _ => throw new InvalidOperationException("Unterstützt werden aktuell CSV-, TXT- und XLSX-Dateien.")
        };
    }

    public Dictionary<string, string?> SuggestMappings(IReadOnlyList<string> headers)
    {
        var targets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["EmployeeNumber"] = Find(headers, "personalnummer", "mitarbeiternummer", "employee number", "employee id", "id"),
            ["FirstName"] = Find(headers, "vorname", "first name", "firstname"),
            ["LastName"] = Find(headers, "nachname", "last name", "lastname", "surname"),
            ["Email"] = Find(headers, "e-mail", "email", "mail"),
            ["PhoneNumber"] = Find(headers, "telefon", "telefonnummer", "phone", "mobile", "mobil"),
            ["WeeklyHours"] = Find(headers, "wochenstunden", "stunden/woche", "weekly hours", "weeklyhours"),
            ["Position"] = Find(headers, "position", "funktion", "job title", "rolle"),
            ["Location"] = Find(headers, "standort", "filiale", "location", "site", "branch"),
            ["Department"] = Find(headers, "abteilung", "bereich", "department", "team"),
            ["Qualifications"] = Find(headers, "qualifikationen", "qualifikation", "skills", "qualifications")
        };

        return targets;
    }

    private static string? Find(IReadOnlyList<string> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var exact = headers.FirstOrDefault(x => Normalize(x) == Normalize(alias));
            if (exact is not null)
                return exact;
        }

        foreach (var alias in aliases)
        {
            var partial = headers.FirstOrDefault(x => Normalize(x).Contains(Normalize(alias), StringComparison.Ordinal));
            if (partial is not null)
                return partial;
        }

        return null;
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static async Task<ImportTable> ReadCsvAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            throw new InvalidOperationException("Die Datei enthält keine Daten.");

        var delimiter = DetectDelimiter(lines[0]);
        var parsed = lines.Select(x => ParseDelimitedLine(x, delimiter)).ToList();
        var headers = MakeUniqueHeaders(parsed[0]);
        var rows = parsed.Skip(1)
            .Where(x => x.Any(v => !string.IsNullOrWhiteSpace(v)))
            .Select(x => Pad(x, headers.Count))
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new ImportTable(fileName, headers, rows);
    }

    private static char DetectDelimiter(string header)
    {
        var candidates = new[] { ';', ',', '\t', '|' };
        return candidates.OrderByDescending(c => header.Count(x => x == c)).First();
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == delimiter && !quoted)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    private static async Task<ImportTable> ReadXlsxAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = await ReadSharedStringsAsync(archive, cancellationToken);
        var worksheetPath = GetFirstWorksheetPath(archive);
        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException("Das erste Tabellenblatt konnte nicht gelesen werden.");

        await using var sheetStream = worksheetEntry.Open();
        var document = await XDocument.LoadAsync(sheetStream, LoadOptions.None, cancellationToken);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var rows = new List<List<string>>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var values = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? string.Empty;
                var columnIndex = ColumnIndex(reference);
                values[columnIndex] = CellValue(cell, ns, sharedStrings);
            }

            if (values.Count == 0)
                continue;

            var width = values.Keys.Max() + 1;
            var data = Enumerable.Repeat(string.Empty, width).ToList();
            foreach (var pair in values)
                data[pair.Key] = pair.Value;
            rows.Add(data);
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("Das Tabellenblatt enthält keine Daten.");

        var headers = MakeUniqueHeaders(rows[0]);
        var dataRows = rows.Skip(1)
            .Where(x => x.Any(v => !string.IsNullOrWhiteSpace(v)))
            .Select(x => Pad(x, headers.Count))
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new ImportTable(fileName, headers, dataRows);
    }

    private static async Task<List<string>> ReadSharedStringsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];

        await using var stream = entry.Open();
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string GetFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("Die Excel-Arbeitsmappe ist ungültig.");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("Die Excel-Arbeitsmappe enthält keine Blattbeziehungen.");

        XDocument workbook;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        XDocument relationships;
        using (var stream = relationshipsEntry.Open()) relationships = XDocument.Load(stream);

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault()
            ?? throw new InvalidOperationException("Die Arbeitsmappe enthält kein Tabellenblatt.");
        var relationId = (string?)firstSheet.Attribute(rel + "id")
            ?? throw new InvalidOperationException("Das Tabellenblatt besitzt keine Beziehung.");
        var target = relationships.Descendants(packageRel + "Relationship")
            .FirstOrDefault(x => (string?)x.Attribute("Id") == relationId)?.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("Das Tabellenblatt konnte nicht aufgelöst werden.");

        target = target.Replace('\\', '/');
        if (target.StartsWith('/'))
            return target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target.TrimStart('/');
    }

    private static string CellValue(XElement cell, XNamespace ns, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(ns + "t").Select(x => x.Value)).Trim();

        var raw = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index].Trim();
        if (type == "b")
            return raw == "1" ? "Ja" : "Nein";
        return raw.Trim();
    }

    private static int ColumnIndex(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var index = 0;
        foreach (var c in letters)
            index = index * 26 + (c - 'A' + 1);
        return Math.Max(0, index - 1);
    }

    private static List<string> MakeUniqueHeaders(IReadOnlyList<string> source)
    {
        var result = new List<string>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < source.Count; i++)
        {
            var value = string.IsNullOrWhiteSpace(source[i]) ? $"Spalte {i + 1}" : source[i].Trim();
            if (!used.TryAdd(value, 1))
            {
                used[value]++;
                value = $"{value} ({used[value]})";
            }
            result.Add(value);
        }
        return result;
    }

    private static List<string> Pad(IReadOnlyList<string> row, int width)
    {
        var result = row.Take(width).ToList();
        while (result.Count < width)
            result.Add(string.Empty);
        return result;
    }
}
