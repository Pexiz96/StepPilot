namespace StepPilot.Services;

public sealed record PublicHolidayInfo(string Name, DateOnly Date, string LegalScope);

public sealed class GermanPublicHolidayService
{
    public PublicHolidayInfo? GetHoliday(DateOnly date, string? federalStateCode)
    {
        var year = date.Year;
        var easterSunday = EasterSunday(year);

        var nationwide = new Dictionary<DateOnly, string>
        {
            [new DateOnly(year, 1, 1)] = "Neujahr",
            [easterSunday.AddDays(-2)] = "Karfreitag",
            [easterSunday.AddDays(1)] = "Ostermontag",
            [new DateOnly(year, 5, 1)] = "Tag der Arbeit",
            [easterSunday.AddDays(39)] = "Christi Himmelfahrt",
            [easterSunday.AddDays(50)] = "Pfingstmontag",
            [new DateOnly(year, 10, 3)] = "Tag der Deutschen Einheit",
            [new DateOnly(year, 12, 25)] = "1. Weihnachtstag",
            [new DateOnly(year, 12, 26)] = "2. Weihnachtstag"
        };

        if (nationwide.TryGetValue(date, out var nationwideName))
            return new PublicHolidayInfo(nationwideName, date, "bundesweit");

        if (string.IsNullOrWhiteSpace(federalStateCode))
            return null;

        var state = federalStateCode.Trim().ToUpperInvariant();
        var stateHolidays = new Dictionary<DateOnly, string>();

        void Add(DateOnly holidayDate, string name) => stateHolidays[holidayDate] = name;

        if (state is "DE-BW" or "DE-BY" or "DE-ST")
            Add(new DateOnly(year, 1, 6), "Heilige Drei Könige");

        if (state == "DE-BE")
            Add(new DateOnly(year, 3, 8), "Internationaler Frauentag");

        if (state is "DE-BW" or "DE-BY" or "DE-HE" or "DE-NW" or "DE-RP" or "DE-SL")
            Add(easterSunday.AddDays(60), "Fronleichnam");

        if (state == "DE-SL")
            Add(new DateOnly(year, 8, 15), "Mariä Himmelfahrt");

        if (state == "DE-TH")
            Add(new DateOnly(year, 9, 20), "Weltkindertag");

        if (state is "DE-BB" or "DE-MV" or "DE-SN" or "DE-ST" or "DE-TH" or "DE-HB" or "DE-HH" or "DE-NI" or "DE-SH")
            Add(new DateOnly(year, 10, 31), "Reformationstag");

        if (state is "DE-BW" or "DE-BY" or "DE-NW" or "DE-RP" or "DE-SL")
            Add(new DateOnly(year, 11, 1), "Allerheiligen");

        if (state == "DE-SN")
            Add(BussUndBettag(year), "Buß- und Bettag");

        return stateHolidays.TryGetValue(date, out var stateName)
            ? new PublicHolidayInfo(stateName, date, state)
            : null;
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly BussUndBettag(int year)
    {
        var november23 = new DateOnly(year, 11, 23);
        var daysBack = ((int)november23.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
        if (daysBack == 0)
            daysBack = 7;
        return november23.AddDays(-daysBack);
    }
}
