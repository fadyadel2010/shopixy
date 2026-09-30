namespace PointOfSale.Infrastructure.Services;

public static class AbandonedCartScheduleTime
{
    private static readonly IReadOnlyDictionary<string, string> CountryTimeZones =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EG"] = "Africa/Cairo",
            ["SA"] = "Asia/Riyadh",
            ["AE"] = "Asia/Dubai",
            ["KW"] = "Asia/Kuwait",
            ["QA"] = "Asia/Qatar",
            ["BH"] = "Asia/Bahrain",
            ["OM"] = "Asia/Muscat",
            ["JO"] = "Asia/Amman",
            ["LB"] = "Asia/Beirut",
            ["US"] = "America/New_York",
            ["GB"] = "Europe/London",
            ["DE"] = "Europe/Berlin",
            ["FR"] = "Europe/Paris"
        };

    public static string ResolveTimeZoneId(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && CountryTimeZones.TryGetValue(countryCode, out var timeZoneId)
            ? timeZoneId
            : "UTC";

    public static DateTime MoveIntoWindow(
        DateTime candidateUtc,
        TimeSpan windowStart,
        TimeSpan windowEnd,
        string timeZoneId)
    {
        if (candidateUtc.Kind != DateTimeKind.Utc)
        {
            candidateUtc = DateTime.SpecifyKind(candidateUtc, DateTimeKind.Utc);
        }

        var timeZone = FindTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(candidateUtc, timeZone);
        var localTime = local.TimeOfDay;
        var overnight = windowStart > windowEnd;
        var insideWindow = overnight
            ? localTime >= windowStart || localTime < windowEnd
            : localTime >= windowStart && localTime < windowEnd;

        if (insideWindow)
        {
            return candidateUtc;
        }

        var targetDate = overnight
            ? localTime >= windowEnd && localTime < windowStart ? local.Date : local.Date.AddDays(1)
            : localTime < windowStart ? local.Date : local.Date.AddDays(1);
        var targetLocal = DateTime.SpecifyKind(targetDate.Add(windowStart), DateTimeKind.Unspecified);

        // A sending-window boundary can fall in a daylight-saving gap. Advance to
        // the first valid local minute, and choose the earliest UTC instant when ambiguous.
        while (timeZone.IsInvalidTime(targetLocal))
        {
            targetLocal = targetLocal.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(targetLocal))
        {
            var offset = timeZone.GetAmbiguousTimeOffsets(targetLocal).Max();
            return new DateTimeOffset(targetLocal, offset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(targetLocal, timeZone);
    }

    public static bool IsInsideWindow(
        DateTime utcNow,
        TimeSpan windowStart,
        TimeSpan windowEnd,
        string timeZoneId) =>
        MoveIntoWindow(utcNow, windowStart, windowEnd, timeZoneId) == utcNow;

    private static TimeZoneInfo FindTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
