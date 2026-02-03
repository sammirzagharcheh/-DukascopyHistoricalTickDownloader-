# HistoricalData

C# console app that downloads Dukascopy historical tick data (.bi5 LZMA), converts it to MT5 bars, and exports CSV + HST (MT5 build 5430 compatible layout). Uses a local data pool to cache raw Dukascopy files for incremental updates.

## Requirements

- .NET 10 SDK

## Usage

### Interactive

Run without arguments to be prompted for:

- Instrument (default EURUSD)
- Start / End (ISO 8601)
- Timeframe (m1, m5, m15, m30, h1)
- Download mode (default Tick->M1)
- Output format (default CSV+HST)
- UTC offset (default +00:00)
- Data pool path (default /DataPool)
- Output path (default ./output)

### CLI arguments

```text
--instrument EURUSD
--start 2025-01-01T00:00:00Z
--end 2025-01-03T00:00:00Z
--timeframe m1|m5|m15|m30|h1
--mode direct|ticks
--format csv|csv+hst
--offset +02:00
--pool /DataPool
--output ./output
--instruments ./config/instruments.json
--http ./config/http.json
--no-prompt
--quiet
--help
```

### Sample run

```text
dotnet run --project c:\sampleApp\HistoricalData\HistoricalData.csproj -- --instrument EURUSD --start 2025-01-01T00:00:00Z --end 2025-01-03T00:00:00Z --timeframe m15 --mode ticks --format csv+hst --offset +00:00 --pool /DataPool --output ./output --no-prompt
```

## Config

### Instruments

[config/instruments.json](config/instruments.json) maps symbol to digits.

### HTTP

[config/http.json](config/http.json) configures base URLs, retry, and timeout.

## Notes

- Tick files use .bi5 LZMA compression.
- Direct M1 mode downloads daily `BID_candles_min_1.bi5` files.
- M1 fallback is used if tick download fails.
- Weekend bars are filtered.
- UTC offset is applied to output alignment.
- Ctrl+C cancels the run gracefully.

## Direct vs Ticks

- `--mode ticks` downloads hourly tick files (`HHh_ticks.bi5`) and aggregates to M1 before resampling to the requested timeframe.
- `--mode direct` downloads daily M1 bars (`BID_candles_min_1.bi5`) and resamples to the requested timeframe.
- If tick download fails and fallback is enabled, the app uses daily M1 bars for that day.

## Data Pool Structure

Downloaded files are cached under the data pool path:

```text
<pool>/<instrument>/<year>/<month>/<day>/
```

Examples:

```text
DataPool/EURUSD/2025/00/01/00h_ticks.bi5
DataPool/EURUSD/2025/00/01/BID_candles_min_1.bi5
```

## Output

- CSV: SYMBOL_TIMEFRAME.csv
- HST: SYMBOL_TIMEFRAME.hst

## Output formats

### CSV (MT5-compatible)

Each row represents one bar with these columns in order:

1. Date in yyyy.MM.dd
2. Time in HH:mm
3. Open
4. High
5. Low
6. Close
7. Tick volume
8. Spread (in points)
9. Real volume

Times are aligned using the configured UTC offset, and weekend bars are filtered.

### HST (MT5 build 5430)

The HST file uses version 501 with this layout:

- Header: version, copyright, symbol, timeframe (minutes), digits,
  last sync time, last bar time, and reserved fields.
- Records: time (Unix seconds), open, high, low, close, volume,
  spread, real volume.

Times are aligned using the configured UTC offset, and prices are rounded
to the symbol digits from config/instruments.json.

## Tests

```text
dotnet test
```
