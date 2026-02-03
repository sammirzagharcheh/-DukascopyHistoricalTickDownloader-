using HistoricalData.Models;

namespace HistoricalData.Export;

public static class BarResampler
{
    public static IReadOnlyList<Bar> Resample(IReadOnlyList<Bar> bars, int timeframeMinutes)
    {
        if (timeframeMinutes <= 1 || bars.Count == 0)
        {
            return bars;
        }

        var result = new List<Bar>();
        Bar? current = null;
        DateTimeOffset currentBucket = default;

        foreach (var bar in bars.OrderBy(b => b.Time))
        {
            var bucket = new DateTimeOffset(
                bar.Time.Year,
                bar.Time.Month,
                bar.Time.Day,
                bar.Time.Hour,
                (bar.Time.Minute / timeframeMinutes) * timeframeMinutes,
                0,
                bar.Time.Offset);

            if (current is null || bucket != currentBucket)
            {
                if (current is not null)
                {
                    result.Add(current);
                }

                currentBucket = bucket;
                current = new Bar(
                    bucket,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume,
                    bar.Spread,
                    bar.RealVolume);
                continue;
            }

            current = current with
            {
                High = Math.Max(current.High, bar.High),
                Low = Math.Min(current.Low, bar.Low),
                Close = bar.Close,
                Volume = current.Volume + bar.Volume,
                Spread = Math.Max(current.Spread, bar.Spread),
                RealVolume = current.RealVolume + bar.RealVolume
            };
        }

        if (current is not null)
        {
            result.Add(current);
        }

        return result;
    }
}
