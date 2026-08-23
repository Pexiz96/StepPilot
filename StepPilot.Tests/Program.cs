using StepPilot.Services;

var failures = new List<string>();

Check("WorkedMinutes subtracts break", TimeTrackingRules.CalculateWorkedMinutes(
    new DateTime(2026, 8, 23, 6, 0, 0, DateTimeKind.Utc),
    new DateTime(2026, 8, 23, 14, 30, 0, DateTimeKind.Utc),
    30) == 480);

Check("WorkedMinutes never negative", TimeTrackingRules.CalculateWorkedMinutes(
    new DateTime(2026, 8, 23, 6, 0, 0, DateTimeKind.Utc),
    new DateTime(2026, 8, 23, 6, 20, 0, DateTimeKind.Utc),
    30) == 0);

Check("Break after exactly six hours remains zero", TimeTrackingRules.RequiredBreakMinutes(360, 30, 45) == 0);
Check("Break after more than six hours is 30", TimeTrackingRules.RequiredBreakMinutes(361, 30, 45) == 30);
Check("Break after more than nine hours is 45", TimeTrackingRules.RequiredBreakMinutes(541, 30, 45) == 45);

Check("Rest hours calculates overnight gap", TimeTrackingRules.RestHours(
    new DateTime(2026, 8, 23, 18, 0, 0, DateTimeKind.Utc),
    new DateTime(2026, 8, 24, 5, 0, 0, DateTimeKind.Utc)) == 11m);

Check("Rest hours never negative", TimeTrackingRules.RestHours(
    new DateTime(2026, 8, 23, 18, 0, 0, DateTimeKind.Utc),
    new DateTime(2026, 8, 23, 17, 0, 0, DateTimeKind.Utc)) == 0m);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Regression checks failed: {failures.Count}");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("All StepPilot regression checks passed.");
return 0;

void Check(string name, bool condition)
{
    if (!condition) failures.Add(name);
}
