using HistoricalData.Config;
using HistoricalData.DataPool;
using HistoricalData.Export;
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

        var aggregator = new BarAggregator("m1", digits, options.UtcOffset, options.FilterWeekends, startUtc, endUtc);

        if (options.DownloadMode == DownloadMode.TickToM1)
        {
            await client.DownloadTicksAndAggregate(
                options.Instrument,
                startUtc,
                endUtc,
                digits,
                options.FallbackToM1,
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
                aggregator,
                summary,
                cts.Token);
        }

        var bars = aggregator.GetBars();
        if (timeframeMinutes > 1)
        {
            bars = BarResampler.Resample(bars, timeframeMinutes);
        }
        summary.Bars = bars.Count;

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
        Console.WriteLine("  --instruments ./config/instruments.json");
        Console.WriteLine("  --http ./config/http.json");
        Console.WriteLine("  --no-prompt");
        Console.WriteLine("  --quiet");
    }
}
