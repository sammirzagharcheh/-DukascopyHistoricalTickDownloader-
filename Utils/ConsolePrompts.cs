namespace HistoricalData.Utils;

public static class ConsolePrompts
{
    public static AppOptions FillMissing(AppOptions options)
    {
        var defaults = AppOptions.Defaults;

        var instrument = Prompt("Instrument", options.Instrument, defaults.Instrument);
        var start = PromptDateTime("Start (ISO 8601)", options.Start, defaults.Start);
        var end = PromptDateTime("End (ISO 8601)", options.End, defaults.End);
        var timeframe = Prompt("Timeframe", options.Timeframe, defaults.Timeframe);

        var mode = PromptOption("Download mode", new[] { "A) Tick->M1", "B) Direct M1" },
            options.DownloadMode == DownloadMode.TickToM1 ? "A" : "B", "A");
        var downloadMode = mode == "B" ? DownloadMode.DirectM1 : DownloadMode.TickToM1;

        var format = PromptOption("Output format", new[] { "A) CSV", "B) CSV+HST" }, "B", "B");
        var outputFormat = format == "A" ? OutputFormat.CsvOnly : OutputFormat.CsvHst;

        var offsetText = Prompt("UTC offset (+hh:mm)", options.UtcOffset.ToString(), defaults.UtcOffset.ToString());
        var offset = TimeSpanParser.TryParse(offsetText, defaults.UtcOffset);

        var dataPool = Prompt("Data pool path", options.DataPoolPath, defaults.DataPoolPath);
        var output = Prompt("Output path", options.OutputPath, defaults.OutputPath);
        var refreshCache = PromptBool("Refresh cache", options.RefreshCache);
        var deduplicateTicks = PromptBool("Deduplicate ticks", options.DeduplicateTicks);
        var skipFallbackIfTicked = PromptBool("Skip fallback overlap", options.SkipFallbackIfTicked);

        return options with
        {
            Instrument = instrument,
            Start = start,
            End = end,
            Timeframe = timeframe,
            DownloadMode = downloadMode,
            OutputFormat = outputFormat,
            UtcOffset = offset,
            DataPoolPath = dataPool,
            OutputPath = output,
            RefreshCache = refreshCache,
            DeduplicateTicks = deduplicateTicks,
            SkipFallbackIfTicked = skipFallbackIfTicked
        };
    }

    private static string Prompt(string label, string current, string fallback)
    {
        Console.Write($"{label} [{current}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.IsNullOrWhiteSpace(current) ? fallback : current;
        }

        return input.Trim();
    }

    private static DateTimeOffset PromptDateTime(string label, DateTimeOffset current, DateTimeOffset fallback)
    {
        Console.Write($"{label} [{current:O}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return current;
        }

        return DateTimeParser.TryParse(input, current == default ? fallback : current);
    }

    private static string PromptOption(string label, string[] options, string current, string fallback)
    {
        Console.WriteLine(label + ":");
        foreach (var option in options)
        {
            Console.WriteLine("  " + option);
        }
        Console.Write($"Select [{current}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.IsNullOrWhiteSpace(current) ? fallback : current;
        }

        return input.Trim().ToUpperInvariant();
    }

    private static bool PromptBool(string label, bool current)
    {
        var currentText = current ? "Y" : "N";
        Console.Write($"{label} (Y/N) [{currentText}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return current;
        }

        return input.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)
               || input.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase)
               || input.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)
               || input.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
               || input.Trim().Equals("ON", StringComparison.OrdinalIgnoreCase);
    }
}
