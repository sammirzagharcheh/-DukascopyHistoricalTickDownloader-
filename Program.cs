using HistoricalData.Config;
using HistoricalData.DataPool;
using HistoricalData.Export;
using HistoricalData.Utils;

namespace HistoricalData;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var argMap = ArgParser.Parse(args);
        var options = AppOptions.FromArgs(argMap);

        if (!options.NonInteractive)
        {
            options = ConsolePrompts.FillMissing(options);
        }

        if (!options.Timeframe.Equals("m1", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Only M1 timeframe is supported.");
            return 1;
        }

        var httpConfig = HttpConfig.Load(options.HttpConfigPath);
        var instrumentConfig = InstrumentConfig.Load(options.InstrumentsPath);
        var digits = instrumentConfig.GetDigits(options.Instrument);

        var poolPath = PathUtils.NormalizePath(options.DataPoolPath);
        var outputPath = PathUtils.NormalizePath(options.OutputPath);
        Directory.CreateDirectory(poolPath);
        Directory.CreateDirectory(outputPath);

        var client = new DukascopyClient(httpConfig, poolPath, options.Verbose);
        var summary = new SummaryReport();

        var startUtc = options.Start.ToUniversalTime();
        var endUtc = options.End.ToUniversalTime();
        if (endUtc < startUtc)
        {
            Console.WriteLine("End time must be after start time.");
            return 1;
        }

        var aggregator = new BarAggregator(options.Timeframe, digits, options.UtcOffset, options.FilterWeekends, startUtc, endUtc);

        if (options.DownloadMode == DownloadMode.TickToM1)
        {
            await client.DownloadTicksAndAggregate(options.Instrument, startUtc, endUtc, digits, options.FallbackToM1, aggregator, summary);
        }
        else
        {
            await client.DownloadM1Bars(options.Instrument, startUtc, endUtc, digits, aggregator, summary);
        }

        var bars = aggregator.GetBars();
        summary.Bars = bars.Count;

        var csvPath = Path.Combine(outputPath, $"{options.Instrument}_{options.Timeframe}.csv");
        CsvWriter.Write(csvPath, bars);

        string? hstPath = null;
        if (options.OutputFormat == OutputFormat.CsvHst)
        {
            hstPath = Path.Combine(outputPath, $"{options.Instrument}_{options.Timeframe}.hst");
            HstWriter.Write(hstPath, bars, options.Instrument, digits, 1);
        }

        summary.Print();
        Console.WriteLine($"CSV: {csvPath}");
        if (hstPath is not null)
        {
            Console.WriteLine($"HST: {hstPath}");
        }

        return 0;
    }
}
