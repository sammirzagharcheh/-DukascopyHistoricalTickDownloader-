using HistoricalData.Config;
using HistoricalData.DataPool;
using HistoricalData.Export;
using HistoricalData.Models;
using HistoricalData.Utils;

namespace HistoricalData;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => arg is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        var argMap = ArgParser.Parse(args);
        var options = AppOptions.FromArgs(argMap);

        if (!options.NonInteractive)
        {
            options = ConsolePrompts.FillMissing(options);
        }

        if (!TimeframeUtils.TryGetMinutes(options.Timeframe, out _))
        {
            Console.WriteLine("Unsupported timeframe. Use m1, m5, m15, m30, or h1.");
            return 1;
        }

        var httpConfig = HttpConfig.Load(options.HttpConfigPath);
        var instrumentConfig = InstrumentConfig.Load(options.InstrumentsPath);
        if (!instrumentConfig.TryGetDigits(options.Instrument, out var digits))
        {
            Console.WriteLine($"Unknown instrument '{options.Instrument}'.");
            Console.WriteLine("Update the instruments config or select a supported symbol.");
            return 1;
        }

        var poolPath = PathUtils.NormalizePath(options.DataPoolPath);
        var outputPath = PathUtils.NormalizePath(options.OutputPath);
        Directory.CreateDirectory(poolPath);
        Directory.CreateDirectory(outputPath);

        var client = new DukascopyClient(httpConfig, poolPath, options.Verbose);
        var summary = new SummaryReport();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Cancellation requested. Finishing current operation...");
        };

        var startUtc = options.Start.ToUniversalTime();
        var endUtc = options.End.ToUniversalTime();
        if (endUtc < startUtc)
        {
            Console.WriteLine("End time must be after start time.");
            return 1;
        }

        if (!TimeframeUtils.TryGetMinutes(options.Timeframe, out var timeframeMinutes))
        {
            Console.WriteLine("Unsupported timeframe. Use m1, m5, m15, m30, or h1.");
            return 1;
        }

        SessionConfig.SessionCalendar? sessionCalendar = null;
        if (options.UseSessionCalendar)
        {
            var sessionConfig = SessionConfig.Load(options.SessionConfigPath);
            sessionCalendar = new SessionConfig.SessionCalendar(sessionConfig);
        }

        var aggregator = new BarAggregator(
            "m1",
            digits,
            options.UtcOffset,
            options.FilterWeekends,
            startUtc,
            endUtc,
            options.DeduplicateTicks,
            options.SkipFallbackIfTicked,
            sessionCalendar);

        if (options.DownloadMode == DownloadMode.TickToM1)
        {
            await client.DownloadTicksAndAggregate(
                options.Instrument,
                startUtc,
                endUtc,
                digits,
                options.FallbackToM1,
                options.RefreshCache,
                options.VerifyChecksum,
                options.RecentRefreshDays,
                aggregator,
                summary,
                cts.Token);
        }
        else
        {
            await client.DownloadM1Bars(
                options.Instrument,
                startUtc,
                endUtc,
                digits,
                options.RefreshCache,
                options.VerifyChecksum,
                options.RecentRefreshDays,
                aggregator,
                summary,
                cts.Token);
        }

        var m1Bars = aggregator.GetBars();
        if (options.RepairGaps && options.DownloadMode == DownloadMode.TickToM1)
        {
            var repairAggregator = new BarAggregator(
                "m1",
                digits,
                options.UtcOffset,
                options.FilterWeekends,
                startUtc,
                endUtc,
                deduplicateTicks: false,
                skipFallbackIfTicked: true,
                sessionCalendar: sessionCalendar);
            var repairSummary = new SummaryReport();
            await client.DownloadM1Bars(
                options.Instrument,
                startUtc,
                endUtc,
                digits,
                options.RefreshCache,
                options.VerifyChecksum,
                options.RecentRefreshDays,
                repairAggregator,
                repairSummary,
                cts.Token);

            var repairBars = repairAggregator.GetBars();
            var added = 0;
            foreach (var bar in repairBars)
            {
                if (aggregator.TryAddFallbackBar(bar, onlyIfMissing: true))
                {
                    added++;
                }
            }

            summary.GapRepairBarsAdded = added;
            summary.GapRepairBarsSkipped = repairBars.Count - added;
            m1Bars = aggregator.GetBars();
        }

        if (options.ValidateM1 && options.DownloadMode == DownloadMode.TickToM1)
        {
            var validateAggregator = new BarAggregator(
                "m1",
                digits,
                options.UtcOffset,
                options.FilterWeekends,
                startUtc,
                endUtc,
                deduplicateTicks: false,
                skipFallbackIfTicked: false,
                sessionCalendar: sessionCalendar);
            var validateSummary = new SummaryReport();
            await client.DownloadM1Bars(
                options.Instrument,
                startUtc,
                endUtc,
                digits,
                options.RefreshCache,
                options.VerifyChecksum,
                options.RecentRefreshDays,
                validateAggregator,
                validateSummary,
                cts.Token);

            var validationBars = validateAggregator.GetBars();
            var barMap = m1Bars.ToDictionary(b => b.Time);
            var tolerance = options.ValidationTolerancePoints / Math.Pow(10, digits);
            foreach (var bar in validationBars)
            {
                summary.ValidationChecked++;
                if (!barMap.TryGetValue(bar.Time, out var baseBar) || !IsWithinTolerance(baseBar, bar, tolerance))
                {
                    summary.ValidationMismatches++;
                }
            }
        }

        var bars = m1Bars;
        if (timeframeMinutes > 1)
        {
            bars = BarResampler.Resample(bars, timeframeMinutes);
        }
        summary.Bars = bars.Count;
        summary.DuplicateTicksDropped = aggregator.DuplicateTicksDropped;
        summary.FallbackBarsSkipped = aggregator.FallbackBarsSkipped;

        var csvPath = Path.Combine(outputPath, $"{options.Instrument}_{options.Timeframe}.csv");
        CsvWriter.Write(csvPath, bars);

        string? hstPath = null;
        if (options.OutputFormat == OutputFormat.CsvHst)
        {
            hstPath = Path.Combine(outputPath, $"{options.Instrument}_{options.Timeframe}.hst");
            HstWriter.Write(hstPath, bars, options.Instrument, digits, timeframeMinutes);
        }

        summary.Print();
        Console.WriteLine($"CSV: {csvPath}");
        if (hstPath is not null)
        {
            Console.WriteLine($"HST: {hstPath}");
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Dukascopy Historical Tick Downloader");
        Console.WriteLine("Usage:");
        Console.WriteLine("  --instrument EURUSD");
        Console.WriteLine("  --start 2025-01-01T00:00:00Z");
        Console.WriteLine("  --end 2025-01-03T00:00:00Z");
        Console.WriteLine("  --timeframe m1");
        Console.WriteLine("  --mode direct|ticks");
        Console.WriteLine("  --format csv|csv+hst");
        Console.WriteLine("  --offset +02:00");
        Console.WriteLine("  --pool /DataPool");
        Console.WriteLine("  --output ./output");
        Console.WriteLine("  --instruments ./src/ConsoleApp/Config/instruments.json");
        Console.WriteLine("  --http ./src/ConsoleApp/Config/http.json");
        Console.WriteLine("  --no-refresh");
        Console.WriteLine("  --recent-refresh-days 30");
        Console.WriteLine("  --verify-checksum");
        Console.WriteLine("  --no-verify-checksum");
        Console.WriteLine("  --no-dedupe");
        Console.WriteLine("  --skip-fallback-overlap");
        Console.WriteLine("  --allow-fallback-overlap");
        Console.WriteLine("  --repair-gaps");
        Console.WriteLine("  --no-repair-gaps");
        Console.WriteLine("  --validate-m1");
        Console.WriteLine("  --no-validate-m1");
        Console.WriteLine("  --validation-tolerance-points 1");
        Console.WriteLine("  --use-session-calendar");
        Console.WriteLine("  --no-session-calendar");
        Console.WriteLine("  --session-config ./src/ConsoleApp/Config/sessions.json");
        Console.WriteLine("  --no-prompt");
        Console.WriteLine("  --quiet");
    }

    private static bool IsWithinTolerance(Bar left, Bar right, double tolerance)
    {
        return Math.Abs(left.Open - right.Open) <= tolerance
               && Math.Abs(left.High - right.High) <= tolerance
               && Math.Abs(left.Low - right.Low) <= tolerance
               && Math.Abs(left.Close - right.Close) <= tolerance;
    }
}
