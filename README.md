# Polymarket Crypto Bot

Terminal-first research bot for Polymarket crypto short-window markets.

The first version is intentionally paper-trading only. It scans active crypto
markets, reads Polymarket order books, compares them with live spot movement,
and prints candidate trades with conservative sizing.

## Why paper trading first

The Twitter post describes a high-frequency price-lag strategy. That kind of
edge can disappear quickly, and live execution adds latency, slippage, partial
fills, API limits, bad market parsing, and settlement risk. With a starting
budget of 50 USD, the first goal is not speed; it is proving that the signal is
real after spread and fees.

## Run with .NET

.NET 7 is available on this machine, so the main v0 is a C# terminal
bot with no external packages.

```powershell
dotnet run -- scan --limit 50
```

Watch continuously:

```powershell
dotnet run -- watch --limit 50 --interval 10
```

Watch and record paper trades:

```powershell
dotnet run -- watch --limit 50 --interval 5 --journal data/paper_trades.csv
```

Summarize the paper journal:

```powershell
dotnet run -- stats --journal data/paper_trades.csv
```

Summarize rejected candidates captured by shadow mode:

```powershell
dotnet run -- shadow-stats --shadow-journal data/paper_trades_shadow.csv
```

Use a smaller paper bankroll:

```powershell
dotnet run -- watch --bankroll 50 --max-position 2
```

## Commands

```powershell
dotnet run -- scan --limit 100 --warmup 5
dotnet run -- watch --limit 100 --interval 5 --min-edge 0.04
dotnet run -- watch --limit 50 --interval 5 --min-move-bps 1 --max-market-minutes 6
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_diag.csv
dotnet run -- watch --limit 50 --interval 5 --min-probability 0.60 --journal data/paper_trades_v5.csv
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v8.csv
dotnet run -- shadow-stats --shadow-journal data/paper_trades_v8_shadow.csv
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v9.csv --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v10.csv --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20 --max-no-ask 0.65 --max-yes-ask 0.80
dotnet run -- replay --snapshot-journal data/paper_trades_v11_snapshots.csv --first-signal-only --max-entry-seconds-after-start 90
dotnet run -- analyze --snapshot-journal data/paper_trades_v11_snapshots.csv
```

If `--shadow-journal` is omitted, the bot derives it from `--journal`. For
example, `data/paper_trades_v8.csv` creates `data/paper_trades_v8_shadow.csv`.

## What the v0 does

- Fetches active Polymarket events from the Gamma API.
- Filters likely BTC/ETH/SOL/XRP/BNB short-term crypto markets.
- Extracts YES/NO token IDs when present.
- Reads best bid/ask from the CLOB order book.
- Fetches live spot prices from Coinbase public endpoints.
- Estimates short-term momentum from local samples.
- Prints paper-trade candidates only when the theoretical edge beats spread and
  risk thresholds.
- In `watch`, opens at most one paper position per market and writes `OPEN` /
  `CLOSE` rows to `data/paper_trades.csv`.
- Trades only the side currently favored by spot-vs-open direction by default.
- Limits default trading candidates to short windows up to 6 minutes.
- Waits until after the market window starts before opening paper trades.
- Requires at least 60% estimated directional probability by default.
- `--verbose` prints rejection reasons such as missing order book, spread too
  wide, edge too small, or move too small.
- Records actionable rejected candidates in a shadow journal so we can compare
  looser thresholds without risking real or paper bankroll.
- Supports asymmetric YES/NO rules with `--min-yes-probability`,
  `--min-no-probability`, `--min-yes-edge`, `--min-no-edge`, and
  `--max-yes-ask`, `--max-no-ask`, and `--side-filter`.
- Records full market snapshots with `--snapshot-journal`.
- Can replay snapshot data offline with `dotnet run -- replay`.
- Can sweep candidate rules offline with `dotnet run -- analyze`.
- Supports first-signal discipline with `--first-signal-only`.
- Supports late-entry prevention with `--max-entry-seconds-after-start`.

## Research workflow

Do not start the next parameter tweak directly. First collect replayable
snapshots:

```powershell
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v11.csv --snapshot-journal data/paper_trades_v11_snapshots.csv --first-signal-only --max-entry-seconds-after-start 90 --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20 --max-no-ask 0.65 --max-yes-ask 0.80
```

Then replay the snapshots offline:

```powershell
dotnet run -- replay --snapshot-journal data/paper_trades_v11_snapshots.csv --first-signal-only --max-entry-seconds-after-start 90 --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20 --max-no-ask 0.65 --max-yes-ask 0.80
```

Then run the offline sweep:

```powershell
dotnet run -- analyze --snapshot-journal data/paper_trades_v11_snapshots.csv
```

Replay outcomes are approximate until official Polymarket settlement is added;
they are inferred from the last recorded spot per market.

## Current paper v12 idea

The v11 analysis rejected the baseline and found one candidate family worth a
fresh holdout: very early, cheap, positive-edge NO entries.

```powershell
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v12.csv --snapshot-journal data/paper_trades_v12_snapshots.csv --side-filter NO --min-no-probability 0.52 --min-no-edge 0.00 --max-no-ask 0.55 --max-entry-seconds-after-start 45 --max-entry-seconds-before-end 15
```

Do not tune this during the holdout.

## Current paper v9 idea

The v8 shadow run showed that global shadow entries were not enough, but the
first signal per market and asymmetric thresholds looked stronger. The next
paper-only test should use separate YES/NO thresholds:

```powershell
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v9.csv --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20
```

This is still paper trading. The negative edge limits are intentionally allowed
only because v8 shadow data showed Polymarket prices were not lining up with our
simple probability model in these 5-minute windows.

## Current paper v10 idea

The v9 paper run showed that expensive NO entries hurt the result. The next
paper-only test keeps the asymmetric probability thresholds but caps entry ask
prices:

```powershell
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v10.csv --min-no-probability 0.55 --min-yes-probability 0.58 --min-no-edge -0.20 --min-yes-edge -0.20 --max-no-ask 0.65 --max-yes-ask 0.80
```

## What it does not do yet

- It does not place live orders.
- It does not use private keys.
- It does not copy-trade the target account yet.
- It does not use flash loans.
- It does not claim profitability.

## Next milestones

1. Collect enough shadow candidates to compare thresholds on real market flow.
2. Record every snapshot to SQLite.
3. Backtest the exact entry and exit logic.
4. Add copy-trading analytics for the target wallet.
5. Add WebSocket order-book feeds for lower latency.
6. Add live execution only after paper results are convincing.

## Risk rules for the 50 USD starting bankroll

- Paper trade first.
- Maximum simulated position: 2 USD.
- Do not trade markets with wide spreads.
- Do not trade if order-book size is too small.
- Stop after 5 consecutive losing paper trades until reviewed.
- Never put private keys in the repo.
