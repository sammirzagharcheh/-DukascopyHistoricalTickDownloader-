namespace HistoricalData.Utils;

public static class TimeRangeUtils
{
    public static IEnumerable<DateTimeOffset> EnumerateHours(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var current = new DateTimeOffset(
            startUtc.UtcDateTime.Year,
            startUtc.UtcDateTime.Month,
            startUtc.UtcDateTime.Day,
            startUtc.UtcDateTime.Hour,
            0,
            0,
            TimeSpan.Zero);

        while (current <= endUtc)
        {
            yield return current;
            current = current.AddHours(1);
        }
    }
}
