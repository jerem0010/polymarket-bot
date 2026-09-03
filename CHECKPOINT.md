# Polymarket Bot Checkpoint

Date: 2026-07-10

This checkpoint freezes what we learned from the paper/shadow runs before
starting another parameter test.

## Current Status

- The live paper process was stopped.
- No real-money trading has been enabled.
- Current code supports paper trading, shadow logging, side-specific thresholds,
  and ask caps.
- The data is useful, but not enough to justify live execution.

## Paper Results

| Run | Closed | Wins | Win rate | PnL | ROI | Read |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| v8 | 4 | 4 | 100.0% | +7.16 | +89.5% | Good, but very small sample |
| v9 | 22 | 15 | 68.2% | -1.52 | -3.4% | More volume, not profitable |
| v10 | 7 | 4 | 57.1% | -1.40 | -10.0% | Ask caps helped less than expected |

Combined v8-v10 paper:

| Side | Closed | Wins | Win rate | PnL | ROI |
| --- | ---: | ---: | ---: | ---: | ---: |
| YES | 11 | 10 | 90.9% | +8.35 | +38.0% |
| NO | 22 | 13 | 59.1% | -4.11 | -9.3% |

Important: the combined YES result is encouraging, but still only 11 trades.

## Shadow Results

Raw shadow is not reliable enough by itself:

| Run | Closed | Wins | Win rate | PnL | ROI |
| --- | ---: | ---: | ---: | ---: | ---: |
| v8 shadow | 198 | 124 | 62.6% | -3.12 | -0.8% |
| v9 shadow | 49 | 29 | 59.2% | -1.58 | -1.6% |
| v10 shadow | 32 | 18 | 56.3% | -6.30 | -9.8% |

However, first signal per market was consistently stronger:

| Run | First signals | Wins | Win rate | PnL | ROI |
| --- | ---: | ---: | ---: | ---: | ---: |
| v8 shadow first | 130 | 100 | 76.9% | +51.46 | +20.0% |
| v9 shadow first | 33 | 23 | 69.7% | +9.57 | +14.5% |
| v10 shadow first | 21 | 15 | 71.4% | +5.53 | +13.2% |

This is the most important signal we have: the first actionable moment in a
market seems much better than later chasing.

## What Worked

- Paper-only first was the right decision.
- Shadow mode exposed false assumptions before real money.
- Side-specific stats were necessary; global stats hid important behavior.
- Ask caps were useful diagnostically, even if v10 did not become profitable.
- The improved `shadow-stats` command now shows side, probability, ask ceiling,
  and first-signal views.

## What Did Not Work

- Repeatedly changing YES/NO thresholds based on small samples.
- Treating raw shadow results as a strategy. Raw shadow overcounts and can log
  both sides in the same market.
- Letting the bot enter later in a market after several earlier rejected signals.
- Trusting the simple probability model as if its edge estimate were calibrated.
- Using PnL from local spot settlement as if it were official Polymarket truth.

## Main Defects To Fix

1. Official settlement
   Paper results currently settle using local spot at/after the window end. The
   next version should verify against Polymarket's actual market outcome or a
   clearly documented official reference.

2. Snapshot logging
   The bot only journals paper opens/closes and selected shadow rejects. We need
   to log every scan snapshot: time, slug, side, bid, ask, spread, size, spot,
   open price, seconds since start, seconds to end, distance bps, momentum bps,
   decision, and reason.

3. First-signal discipline
   The data suggests the first actionable signal per market is strongest. The
   strategy should be able to enforce "first signal only" or "do not chase after
   first rejection".

4. Time-window controls
   We have a minimum entry age and avoid the very end, but we do not yet have a
   maximum entry age after market start. Add something like
   `--max-entry-seconds-after-start`.

5. Backtesting / replay
   Parameters should not be changed by live trial-and-error. Once snapshots are
   logged, add a replay command to test thresholds offline on the same data.

6. Calibration
   The current probability formula is a heuristic:
   `distance_from_open + recent_momentum * 0.35`.
   It needs calibration against historical snapshot outcomes before edge should
   drive sizing.

7. Fill realism
   Paper fills assume instant execution at the top ask. Real execution will have
   latency, partial fills, cancellations, and stale books.

## What To Add Next

Priority order:

1. Add a full snapshot journal.
2. Add replay/backtest from snapshots.
3. Add first-signal-only and max-entry-age controls.
4. Add official settlement verification.
5. Run a frozen parameter set on a fresh holdout period.
6. Only then revisit live execution.

## What To Stop Doing For Now

- Stop launching v11/v12 parameter tweaks without replayable data.
- Stop treating one side as permanently better based on one session.
- Stop using negative edge as a real decision signal until the model is
  calibrated.
- Stop discussing real money, private keys, or flash loans until paper is
  stable across larger samples.

## Tentative Next Build

The next build should not be another strategy tweak. It should be a research
upgrade. First pass implemented on 2026-07-10:

- command: `watch` keeps collecting paper and shadow
- new output: `data/*_snapshots.csv`
- new command: `replay`
- new options:
  - `--snapshot-journal data/snapshots.csv`
  - `--first-signal-only`
  - `--max-entry-seconds-after-start 90`

Known limitation: replay outcomes are still approximate. They use the last
recorded spot per market, not official Polymarket settlement.

Second pass implemented on 2026-07-10:

- new command: `analyze`
- offline parameter sweep over side filters, probability thresholds, ask caps,
  entry age, edge threshold, and first-signal behavior
- chronological train/test split for rough overfit detection

The first analyzed dataset rejected the v11 baseline and produced one fresh
holdout hypothesis: early cheap NO with non-negative estimated edge.

Target success metric before live consideration:

- at least 200 fresh markets
- official or verified settlement
- positive ROI after realistic ask/slippage assumptions
- no single side or small time block responsible for all profit
