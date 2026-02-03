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

- CSV: `SYMBOL_m1.csv`
- HST: `SYMBOL_m1.hst`
