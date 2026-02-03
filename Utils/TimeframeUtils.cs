namespace HistoricalData.Utils;

public static class TimeframeUtils
{
    private static readonly Dictionary<string, int> TimeframeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m1"] = 1,
        ["m5"] = 5,
        ["m15"] = 15,
        ["m30"] = 30,
        ["h1"] = 60
    };

    public static bool TryGetMinutes(string timeframe, out int minutes)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            minutes = 0;
            return false;
        }

        return TimeframeMap.TryGetValue(timeframe.Trim(), out minutes);
    }
}
