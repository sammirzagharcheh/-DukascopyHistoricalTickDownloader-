using System.Net;
using HistoricalData.Config;
using HistoricalData.DataPool;
using HistoricalData.Export;
using HistoricalData.Models;
using HistoricalData.Utils;
using SevenZip.Compression.LZMA;

namespace HistoricalData;

public sealed class DukascopyClient
{
    private const string TickFileSuffix = "ticks";
    private const string M1DayFileName = "BID_candles_min_1.bi5";
    private const int TickRecordSize = 20;
    private const int M1RecordSize = 24;
    private readonly HttpConfig _config;
    private readonly DataPool.DataPool _pool;
    private readonly HttpClient _httpClient;
    private readonly bool _verbose;
    private readonly int _maxConcurrency;

    public DukascopyClient(HttpConfig config, string poolPath, bool verbose)
    {
        _config = config;
        _pool = new DataPool.DataPool(poolPath);
        _verbose = verbose;
        _maxConcurrency = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };
    }

    public async Task DownloadTicksAndAggregate(
        string instrument,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int digits,
        bool fallbackToM1,
        bool refreshCache,
        BarAggregator aggregator,
        SummaryReport summary,
        CancellationToken cancellationToken = default)
    {
        var aggregateLock = new object();
        var summaryLock = new object();
        var fallbackLock = new object();
        var fallbackDays = new HashSet<DateTimeOffset>();
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();

        foreach (var hourUtc in TimeRangeUtils.EnumerateHours(startUtc, endUtc))
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessHourAsync(hourUtc);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        async Task ProcessHourAsync(DateTimeOffset hourUtc)
        {
            lock (summaryLock)
            {
                summary.HoursProcessed++;
            }

            var outcome = await DownloadToPoolAsync(instrument, hourUtc, TickFileSuffix, refreshCache, cancellationToken);
            if (outcome.Success && outcome.LocalPath is not null)
            {
                var ticks = await ReadTicksAsync(outcome.LocalPath, hourUtc, digits, cancellationToken);
                if (ticks.Count > 0)
                {
                    lock (aggregateLock)
                    {
                        foreach (var tick in ticks)
                        {
                            aggregator.AddTick(tick);
                        }
                    }

                    lock (summaryLock)
                    {
                        summary.Ticks += ticks.Count;
                    }
                }

                return;
            }

            if (outcome.NotFound)
            {
                lock (summaryLock)
                {
                    summary.MissingHours++;
                }

                if (_verbose)
                {
                    Console.WriteLine($"Missing ticks for {instrument} {hourUtc:yyyy-MM-dd HH}:00Z");
                }
            }

            if (fallbackToM1)
            {
                var dayStart = new DateTimeOffset(hourUtc.Date, TimeSpan.Zero);
                var shouldDownload = false;
                lock (fallbackLock)
                {
                    if (!fallbackDays.Contains(dayStart))
                    {
                        fallbackDays.Add(dayStart);
                        shouldDownload = true;
                    }
                }

                if (!shouldDownload)
                {
                    return;
                }

                var fallbackBars = await DownloadM1BarsForDayData(instrument, dayStart, digits, refreshCache, cancellationToken);
                if (fallbackBars.Count > 0)
                {
                    lock (aggregateLock)
                    {
                        foreach (var bar in fallbackBars)
                        {
                            aggregator.AddBar(bar);
                        }
                    }

                    lock (summaryLock)
                    {
                        summary.M1FallbackBars += fallbackBars.Count;
                    }
                }
            }
        }
    }

    public async Task DownloadM1Bars(
        string instrument,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int digits,
        bool refreshCache,
        BarAggregator aggregator,
        SummaryReport summary,
        CancellationToken cancellationToken = default)
    {
        var aggregateLock = new object();
        var summaryLock = new object();
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();

        foreach (var dayUtc in TimeRangeUtils.EnumerateDays(startUtc, endUtc))
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessDayAsync(dayUtc);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        async Task ProcessDayAsync(DateTimeOffset dayUtc)
        {
            var dayStart = new DateTimeOffset(dayUtc.UtcDateTime, TimeSpan.Zero);
            var dayEnd = dayStart.AddDays(1).AddTicks(-1);
            var hourCount = CountHoursInRange(dayStart, dayEnd, startUtc, endUtc);

            lock (summaryLock)
            {
                summary.HoursProcessed += hourCount;
            }

            var bars = await DownloadM1BarsForDayData(instrument, dayStart, digits, refreshCache, cancellationToken);
            if (bars.Count == 0)
            {
                lock (summaryLock)
                {
                    summary.MissingHours += hourCount;
                }

                return;
            }

            lock (aggregateLock)
            {
                foreach (var bar in bars)
                {
                    aggregator.AddBar(bar);
                }
            }
        }
    }

    private async Task<IReadOnlyList<Bar>> DownloadM1BarsForDayData(
        string instrument,
        DateTimeOffset dayUtc,
        int digits,
        bool refreshCache,
        CancellationToken cancellationToken)
    {
        var outcome = await DownloadDailyToPoolAsync(instrument, dayUtc, M1DayFileName, refreshCache, cancellationToken);
        if (!outcome.Success || outcome.LocalPath is null)
        {
            if (outcome.NotFound && _verbose)
            {
                Console.WriteLine($"Missing M1 bars for {instrument} {dayUtc:yyyy-MM-dd}");
            }

            return Array.Empty<Bar>();
        }

        return await ReadBarsAsync(outcome.LocalPath, dayUtc, digits, cancellationToken);
    }

    private async Task<DownloadOutcome> DownloadToPoolAsync(
        string instrument,
        DateTimeOffset hourUtc,
        string suffix,
        bool refreshCache,
        CancellationToken cancellationToken)
    {
        var fileName = $"{hourUtc:HH}h_{suffix}.bi5";
        var monthCandidates = new List<int>
        {
            hourUtc.Month - 1,
            hourUtc.Month
        }.Where(m => m is >= 0 and <= 12).Distinct().ToList();

        if (!refreshCache)
        {
            foreach (var month in monthCandidates)
            {
                var localPath = _pool.GetLocalPath(instrument, hourUtc.Year, month, hourUtc.Day, fileName);
                if (DataPool.DataPool.HasValidFile(localPath))
                {
                    return DownloadOutcome.FromCache(localPath);
                }
            }
        }

        var anyNotFound = true;
        var lastError = string.Empty;

        for (var attempt = 1; attempt <= _config.RetryCount; attempt++)
        {
            anyNotFound = true;
            foreach (var month in monthCandidates)
            {
                var relativePath = $"{instrument}/{hourUtc:yyyy}/{month:00}/{hourUtc:dd}/{fileName}";
                var localPath = _pool.GetLocalPath(instrument, hourUtc.Year, month, hourUtc.Day, fileName);
                foreach (var baseUrl in _config.BaseUrls)
                {
                    var url = $"{baseUrl.TrimEnd('/')}/{relativePath}";
                    var result = await TryDownloadAsync(url, localPath, cancellationToken);
                    if (result.Success)
                    {
                        return DownloadOutcome.CreateSuccess(localPath);
                    }

                    if (!result.NotFound)
                    {
                        anyNotFound = false;
                        lastError = result.ErrorMessage ?? lastError;
                    }
                }
            }

            if (anyNotFound)
            {
                return DownloadOutcome.CreateNotFound();
            }

            if (attempt < _config.RetryCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryBackoffSeconds), cancellationToken);
            }
        }

        if (_verbose && !string.IsNullOrWhiteSpace(lastError))
        {
            Console.WriteLine(lastError);
        }

        return DownloadOutcome.Failure(lastError);
    }

    private async Task<DownloadOutcome> DownloadDailyToPoolAsync(
        string instrument,
        DateTimeOffset dayUtc,
        string fileName,
        bool refreshCache,
        CancellationToken cancellationToken)
    {
        var monthCandidates = new List<int>
        {
            dayUtc.Month - 1,
            dayUtc.Month
        }.Where(m => m is >= 0 and <= 12).Distinct().ToList();

        if (!refreshCache)
        {
            foreach (var month in monthCandidates)
            {
                var localPath = _pool.GetLocalPath(instrument, dayUtc.Year, month, dayUtc.Day, fileName);
                if (DataPool.DataPool.HasValidFile(localPath))
                {
                    return DownloadOutcome.FromCache(localPath);
                }
            }
        }

        var anyNotFound = true;
        var lastError = string.Empty;

        for (var attempt = 1; attempt <= _config.RetryCount; attempt++)
        {
            anyNotFound = true;
            foreach (var month in monthCandidates)
            {
                var relativePath = $"{instrument}/{dayUtc:yyyy}/{month:00}/{dayUtc:dd}/{fileName}";
                var localPath = _pool.GetLocalPath(instrument, dayUtc.Year, month, dayUtc.Day, fileName);
                foreach (var baseUrl in _config.BaseUrls)
                {
                    var url = $"{baseUrl.TrimEnd('/')}/{relativePath}";
                    var result = await TryDownloadAsync(url, localPath, cancellationToken);
                    if (result.Success)
                    {
                        return DownloadOutcome.CreateSuccess(localPath);
                    }

                    if (!result.NotFound)
                    {
                        anyNotFound = false;
                        lastError = result.ErrorMessage ?? lastError;
                    }
                }
            }

            if (anyNotFound)
            {
                return DownloadOutcome.CreateNotFound();
            }

            if (attempt < _config.RetryCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryBackoffSeconds), cancellationToken);
            }
        }

        if (_verbose && !string.IsNullOrWhiteSpace(lastError))
        {
            Console.WriteLine(lastError);
        }

        return DownloadOutcome.Failure(lastError);
    }

    private async Task<DownloadResult> TryDownloadAsync(string url, string localPath, CancellationToken cancellationToken)
    {
        try
        {
            if (_verbose)
            {
                Console.WriteLine($"Downloading {url}");
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return DownloadResult.CreateNotFound();
            }

            response.EnsureSuccessStatusCode();
            var tempPath = localPath + ".tmp";
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var info = new FileInfo(tempPath);
            if (!info.Exists || info.Length == 0)
            {
                return DownloadResult.Failure("Downloaded file is empty.");
            }

            File.Move(tempPath, localPath, overwrite: true);
            return DownloadResult.CreateSuccess();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DownloadResult.Failure(ex.Message);
        }
    }

    private static async Task<IReadOnlyList<Tick>> ReadTicksAsync(string path, DateTimeOffset hourUtc, int digits, CancellationToken cancellationToken)
    {
        var compressed = await File.ReadAllBytesAsync(path, cancellationToken);
        return await Task.Run(() =>
        {
            var data = DecompressLzma(compressed);
            return ParseTicks(data, hourUtc, digits);
        }, cancellationToken);
    }

    private static async Task<IReadOnlyList<Bar>> ReadBarsAsync(string path, DateTimeOffset hourUtc, int digits, CancellationToken cancellationToken)
    {
        var compressed = await File.ReadAllBytesAsync(path, cancellationToken);
        return await Task.Run(() =>
        {
            var data = DecompressLzma(compressed);
            return ParseBars(data, hourUtc, digits);
        }, cancellationToken);
    }

    internal static IReadOnlyList<Tick> ParseTicks(ReadOnlySpan<byte> data, DateTimeOffset hourUtc, int digits)
    {
        var ticks = new List<Tick>(data.Length / TickRecordSize);
        var scale = Math.Pow(10, digits);

        for (var offset = 0; offset + TickRecordSize <= data.Length; offset += TickRecordSize)
        {
            var ms = BigEndian.ReadInt32(data, offset);
            var bid = BigEndian.ReadInt32(data, offset + 4);
            var ask = BigEndian.ReadInt32(data, offset + 8);
            var bidVol = BigEndian.ReadSingle(data, offset + 12);
            var askVol = BigEndian.ReadSingle(data, offset + 16);

            var time = hourUtc.AddMilliseconds(ms);
            ticks.Add(new Tick(time, bid / scale, ask / scale, bidVol, askVol));
        }

        return ticks;
    }

    internal static IReadOnlyList<Bar> ParseBars(ReadOnlySpan<byte> data, DateTimeOffset hourUtc, int digits)
    {
        var bars = new List<Bar>(data.Length / M1RecordSize);
        var scale = Math.Pow(10, digits);

        for (var offset = 0; offset + M1RecordSize <= data.Length; offset += M1RecordSize)
        {
            var seconds = BigEndian.ReadInt32(data, offset);
            var open = BigEndian.ReadInt32(data, offset + 4);
            var high = BigEndian.ReadInt32(data, offset + 8);
            var low = BigEndian.ReadInt32(data, offset + 12);
            var close = BigEndian.ReadInt32(data, offset + 16);
            var volume = BigEndian.ReadSingle(data, offset + 20);

            var time = hourUtc.AddSeconds(seconds);
            bars.Add(new Bar(
                time,
                open / scale,
                high / scale,
                low / scale,
                close / scale,
                (long)Math.Round(volume),
                0,
                0));
        }

        return bars;
    }

    private static long CountHoursInRange(DateTimeOffset dayStart, DateTimeOffset dayEnd, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var effectiveStart = dayStart > rangeStart ? dayStart : rangeStart;
        var effectiveEnd = dayEnd < rangeEnd ? dayEnd : rangeEnd;
        if (effectiveEnd < effectiveStart)
        {
            return 0;
        }

        return TimeRangeUtils.EnumerateHours(effectiveStart, effectiveEnd).LongCount();
    }

    private static byte[] DecompressLzma(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        var decoder = new Decoder();
        var properties = new byte[5];
        if (input.Read(properties, 0, 5) != 5)
        {
            throw new InvalidDataException("Invalid LZMA properties.");
        }

        decoder.SetDecoderProperties(properties);

        long outSize = 0;
        for (var i = 0; i < 8; i++)
        {
            var b = input.ReadByte();
            if (b < 0)
            {
                throw new EndOfStreamException("Unexpected end of LZMA stream.");
            }

            outSize |= (long)(byte)b << (8 * i);
        }

        using var output = new MemoryStream();
        decoder.Code(input, output, input.Length - input.Position, outSize, null);
        return output.ToArray();
    }

    private sealed record DownloadOutcome(bool Success, bool NotFound, string? LocalPath, string? ErrorMessage)
    {
        public static DownloadOutcome CreateSuccess(string localPath) => new(true, false, localPath, null);

        public static DownloadOutcome FromCache(string localPath) => new(true, false, localPath, null);

        public static DownloadOutcome CreateNotFound() => new(false, true, null, null);

        public static DownloadOutcome Failure(string? error) => new(false, false, null, error);
    }

    private sealed record DownloadResult(bool Success, bool NotFound, string? ErrorMessage)
    {
        public static DownloadResult CreateSuccess() => new(true, false, null);

        public static DownloadResult CreateNotFound() => new(false, true, null);

        public static DownloadResult Failure(string? error) => new(false, false, error);
    }
}
