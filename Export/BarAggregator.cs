using HistoricalData.Models;

namespace HistoricalData.Export;

public sealed class BarAggregator
{
    private readonly Dictionary<DateTimeOffset, BarBuilder> _bars = new();
    private readonly int _digits;
    private readonly double _scale;
    private readonly TimeSpan _utcOffset;
    private readonly bool _filterWeekends;
    private readonly DateTimeOffset _startServer;
    private readonly DateTimeOffset _endServer;

    public BarAggregator(string timeframe, int digits, TimeSpan utcOffset, bool filterWeekends, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        Timeframe = timeframe;
        _digits = digits;
        _scale = Math.Pow(10, digits);
        _utcOffset = utcOffset;
        _filterWeekends = filterWeekends;
        _startServer = startUtc.ToOffset(utcOffset);
        _endServer = endUtc.ToOffset(utcOffset);
    }

    public string Timeframe { get; }

    public void AddTick(Tick tick)
    {
        var serverTime = tick.Time.ToOffset(_utcOffset);
        if (!IsInRange(serverTime))
        {
            return;
        }

        if (_filterWeekends && IsWeekend(serverTime))
        {
            return;
        }

        var minuteTime = new DateTimeOffset(serverTime.Year, serverTime.Month, serverTime.Day, serverTime.Hour, serverTime.Minute, 0, _utcOffset);
        if (!_bars.TryGetValue(minuteTime, out var builder))
        {
            builder = new BarBuilder(minuteTime, _scale);
            _bars[minuteTime] = builder;
        }

        builder.AddTick(tick, serverTime);
    }

    public void AddBar(Bar bar)
    {
        var serverTime = bar.Time.ToOffset(_utcOffset);
        if (!IsInRange(serverTime))
        {
            return;
        }

        if (_filterWeekends && IsWeekend(serverTime))
        {
            return;
        }

        var minuteTime = new DateTimeOffset(serverTime.Year, serverTime.Month, serverTime.Day, serverTime.Hour, serverTime.Minute, 0, _utcOffset);
        if (!_bars.TryGetValue(minuteTime, out var builder))
        {
            builder = new BarBuilder(minuteTime, _scale);
            _bars[minuteTime] = builder;
        }

        builder.MergeBar(bar);
    }

    public IReadOnlyList<Bar> GetBars()
    {
        return _bars.Values
            .Select(b => b.Build(_digits))
            .Where(b => b.Time >= _startServer && b.Time <= _endServer)
            .OrderBy(b => b.Time)
            .ToList();
    }

    private bool IsWeekend(DateTimeOffset time)
    {
        return time.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private bool IsInRange(DateTimeOffset time)
    {
        return time >= _startServer && time <= _endServer;
    }

    private sealed class BarBuilder
    {
        private readonly DateTimeOffset _time;
        private readonly double _scale;
        private bool _hasValue;
        private double _open;
        private double _high;
        private double _low;
        private double _close;
        private long _tickVolume;
        private long _realVolume;
        private int _spread;
        private double _lastBid;
        private double _lastAsk;

        public BarBuilder(DateTimeOffset time, double scale)
        {
            _time = time;
            _scale = scale;
        }

        public void AddTick(Tick tick, DateTimeOffset serverTime)
        {
            var price = tick.Bid;
            if (!_hasValue)
            {
                _open = price;
                _high = price;
                _low = price;
                _close = price;
                _hasValue = true;
            }
            else
            {
                _high = Math.Max(_high, price);
                _low = Math.Min(_low, price);
                _close = price;
            }

            _lastBid = tick.Bid;
            _lastAsk = tick.Ask;
            _tickVolume++;
            _realVolume += (long)Math.Round(tick.BidVolume + tick.AskVolume);
            _spread = (int)Math.Round((_lastAsk - _lastBid) * _scale);
        }

        public void MergeBar(Bar bar)
        {
            if (!_hasValue)
            {
                _open = bar.Open;
                _high = bar.High;
                _low = bar.Low;
                _close = bar.Close;
                _hasValue = true;
            }
            else
            {
                _high = Math.Max(_high, bar.High);
                _low = Math.Min(_low, bar.Low);
                _close = bar.Close;
            }

            _tickVolume += bar.Volume;
            _realVolume += bar.RealVolume;
            _spread = Math.Max(_spread, bar.Spread);
        }

        public Bar Build(int digits)
        {
            return new Bar(
                _time,
                Math.Round(_open, digits),
                Math.Round(_high, digits),
                Math.Round(_low, digits),
                Math.Round(_close, digits),
                _tickVolume,
                _spread,
                _realVolume
            );
        }
    }
}
