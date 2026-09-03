# Snapshot Analysis

Date: 2026-07-10

Dataset analyzed:

- `data/paper_trades_v11.csv`
- `data/paper_trades_v11_shadow.csv`
- `data/paper_trades_v11_snapshots.csv`

The v11 run was stopped before analysis.

## Summary

The current v11 trading config is not good enough.

Baseline v11 replay on snapshots:

| Trades | Wins | Win rate | PnL | ROI |
| ---: | ---: | ---: | ---: | ---: |
| 13 | 8 | 61.5% | -1.47 | -5.7% |

Paper v11:

| Trades | Wins | Win rate | PnL | ROI |
| ---: | ---: | ---: | ---: | ---: |
| 13 | 7 | 53.8% | -4.41 | -17.0% |

The difference between replay and paper is expected because replay still uses
approximate spot-based settlement. Both are negative, so the baseline is
rejected.

## Main Finding

The strongest pattern in the v11 snapshots is not "YES always" or "NO always".
It is:

- early entry
- cheap ask
- preferably non-negative estimated edge
- avoid chasing later in the market

The best-looking NO configs on the same dataset are very early and cheap:

```text
side=NO
maxAge=45s
min NO probability=0.52
max NO ask=0.55-0.60
min NO edge=0.00
```

The best-looking YES configs require stronger probability and moderate ask:

```text
side=YES
min YES probability=0.60
max YES ask=0.70
```

But sample sizes are still small, especially in the train/test split.

## Why Previous Shadow Was Misleading

Raw shadow is not a strategy. It logs rejected opportunities, not the exact
trade policy. The "first shadow per market" stat can look strong while the real
paper policy loses because:

- accepted paper trades are not part of the shadow set
- a shadow entry may be a later rejected signal, not the actual first executable
  trade
- raw shadow can include different selection pressure than paper
- both paper and shadow still settle from local spot, not official Polymarket
  settlement

The snapshot replay is more useful because it can apply one consistent rule to
all observed opportunities.

## Offline Sweep Result

The current `analyze` command tests probability thresholds, ask caps, entry age,
side filters, edge thresholds, and first-signal behavior.

Important result:

```text
Baseline v11:
n=13, pnl=-1.47, roi=-5.7%

Best early cheap NO family:
side=NO, maxAge=45s, pN=0.52, askN<=0.55, edgeN>=0.00
n=7, wins=7, pnl=+13.70, roi=+97.8%
```

Chronological split:

```text
train markets=27, test markets=28
same early cheap NO family:
train n=5, pnl=+9.77
test  n=2, pnl=+3.92
```

This is promising but too small. It is a hypothesis, not proof.

## What To Improve Next

1. Official settlement
   The biggest quality upgrade is to settle paper against the actual market
   result instead of local spot.

2. Larger holdout
   Run a fresh holdout with the candidate config. Do not tune during the run.

3. Replay-first workflow
   Continue collecting snapshots and use `analyze` before changing parameters.

4. More robust validation
   Add more train/test splits or walk-forward validation once there are hundreds
   of markets.

5. Realistic execution
   Later, account for stale quotes, fill delay, partial size, and slippage.

## Candidate Holdout Config

This is the next paper-only candidate, based on current snapshots:

```powershell
dotnet run -- watch --limit 50 --interval 5 --verbose --journal data/paper_trades_v12.csv --snapshot-journal data/paper_trades_v12_snapshots.csv --side-filter NO --min-no-probability 0.52 --min-no-edge 0.00 --max-no-ask 0.55 --max-entry-seconds-after-start 45 --max-entry-seconds-before-end 15
```

This should be treated as a fresh holdout test. It is not ready for real money.

Checkpoint criteria:

- at least 30 closed paper trades, or at least 100 fresh markets observed
- replay and paper both positive
- no single 30-minute period responsible for most profit
- then inspect official settlement before any live execution work
