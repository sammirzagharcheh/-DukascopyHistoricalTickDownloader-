using HistoricalData.Utils;

namespace HistoricalData;

public enum DownloadMode
{
    TickToM1,
    DirectM1
}

public enum OutputFormat
{
    CsvOnly,
    CsvHst
}

public sealed record AppOptions(
    string Instrument,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Timeframe,
    DownloadMode DownloadMode,
    OutputFormat OutputFormat,
    TimeSpan UtcOffset,
    string DataPoolPath,
    string OutputPath,
    string InstrumentsPath,
    string HttpConfigPath,
    bool Verbose,
    bool FilterWeekends,
    bool FallbackToM1,
    bool RefreshCache,
    bool DeduplicateTicks,
    bool SkipFallbackIfTicked,
    bool NonInteractive
)
{
    public static AppOptions Defaults => new(
        Instrument: "EURUSD",
        Start: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        End: new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero),
        Timeframe: "m1",
        DownloadMode: DownloadMode.TickToM1,
        OutputFormat: OutputFormat.CsvHst,
        UtcOffset: TimeSpan.Zero,
        DataPoolPath: "/DataPool",
        OutputPath: "./output",
        InstrumentsPath: "./config/instruments.json",
        HttpConfigPath: "./config/http.json",
        Verbose: true,
        FilterWeekends: true,
        FallbackToM1: true,
        RefreshCache: true,
        DeduplicateTicks: true,
        SkipFallbackIfTicked: true,
        NonInteractive: false
    );

    public static AppOptions FromArgs(Dictionary<string, string> args)
    {
        var d = Defaults;

        var instrument = args.GetValueOrDefault("instrument", d.Instrument);
        var timeframe = args.GetValueOrDefault("timeframe", d.Timeframe);
        var dataPoolPath = args.GetValueOrDefault("pool", d.DataPoolPath);
        var outputPath = args.GetValueOrDefault("output", d.OutputPath);
        var instrumentsPath = args.GetValueOrDefault("instruments", d.InstrumentsPath);
        var httpConfigPath = args.GetValueOrDefault("http", d.HttpConfigPath);

        var start = DateTimeParser.TryParse(args.GetValueOrDefault("start"), d.Start);
        var end = DateTimeParser.TryParse(args.GetValueOrDefault("end"), d.End);

        var mode = args.GetValueOrDefault("mode");
        var downloadMode = mode?.Equals("direct", StringComparison.OrdinalIgnoreCase) == true
            ? DownloadMode.DirectM1
            : mode?.Equals("ticks", StringComparison.OrdinalIgnoreCase) == true
                ? DownloadMode.TickToM1
                : d.DownloadMode;

        var format = args.GetValueOrDefault("format");
        var outputFormat = format?.Equals("csv", StringComparison.OrdinalIgnoreCase) == true
            ? OutputFormat.CsvOnly
            : OutputFormat.CsvHst;

        var offset = TimeSpanParser.TryParse(args.GetValueOrDefault("offset"), d.UtcOffset);
        var verbose = !args.ContainsKey("quiet");
        var nonInteractive = args.ContainsKey("no-prompt");

        var refreshCache = GetBool(args, "refresh", !args.ContainsKey("no-refresh"));
        var deduplicateTicks = GetBool(args, "dedupe", !args.ContainsKey("no-dedupe"));
        var skipFallbackIfTicked = GetBool(args, "skip-fallback-overlap", !args.ContainsKey("allow-fallback-overlap"));

        return d with
        {
            Instrument = instrument,
            Start = start,
            End = end,
            Timeframe = timeframe,
            DownloadMode = downloadMode,
            OutputFormat = outputFormat,
            UtcOffset = offset,
            DataPoolPath = dataPoolPath,
            OutputPath = outputPath,
            InstrumentsPath = instrumentsPath,
            HttpConfigPath = httpConfigPath,
            Verbose = verbose,
            RefreshCache = refreshCache,
            DeduplicateTicks = deduplicateTicks,
            SkipFallbackIfTicked = skipFallbackIfTicked,
            NonInteractive = nonInteractive
        };
    }

    private static bool GetBool(Dictionary<string, string> args, string key, bool fallback)
    {
        if (!args.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("y", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
