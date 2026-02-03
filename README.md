# HistoricalData

C# console app that downloads Dukascopy historical tick data (.bi5 LZMA), converts it to MT5 M1 bars, and exports CSV + HST (MT5 build 5430 compatible layout). Uses a local data pool to cache raw Dukascopy files for incremental updates.

## Requirements

- .NET 10 SDK

## Usage

### Interactive

Run without arguments to be prompted for:

- Instrument (default EURUSD)
- Start / End (ISO 8601)
- Timeframe (m1)
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
--timeframe m1
--mode direct|ticks
--format csv|csv+hst
--offset +02:00
--pool /DataPool
--output ./output
--instruments ./config/instruments.json
--http ./config/http.json
--no-prompt
--quiet
```

### Sample run

```text
dotnet run --project c:\sampleApp\HistoricalData\HistoricalData.csproj -- --instrument EURUSD --start 2025-01-01T00:00:00Z --end 2025-01-03T00:00:00Z --timeframe m1 --mode ticks --format csv+hst --offset +00:00 --pool /DataPool --output ./output --no-prompt
```

## Config

### Instruments

[config/instruments.json](config/instruments.json) maps symbol to digits.

### HTTP

[config/http.json](config/http.json) configures base URLs, retry, and timeout.

## Notes

- Tick files use .bi5 LZMA compression.
- M1 fallback is used if tick download fails.
- Weekend bars are filtered.
- UTC offset is applied to output alignment.

## Output

- CSV: SYMBOL_m1.csv
- HST: SYMBOL_m1.hst

## Output formats

### CSV (MT5-compatible)

Each row represents one M1 bar with these columns in order:

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
