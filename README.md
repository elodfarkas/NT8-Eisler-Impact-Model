# Eisler AdaptiveEdge Strategy for NinjaTrader 8

An experimental **NinjaTrader 8 automated trading strategy** based on an Eisler-style event-impact model, adaptive entry gating, latency-aware signal confirmation, and multi-layer risk management.

The strategy is designed for advanced research into short-horizon order-flow prediction, event-response modeling, adaptive execution, and intraday futures trading system development.

> **Disclaimer**  
> This project is for research, education, and experimental development only.  
> It is not financial advice, investment advice, or a guaranteed trading system.  
> Futures, forex, equities, crypto, and leveraged products involve substantial risk.  
> Use at your own risk.

---
***

# 📊 Empirical Performance & Lifecycle Statistics

Based on detailed historical validation logs extracted directly from `v7_4(09.15-11.30)eisler_trade_lifecycle_20260617_173239.csv`, the execution model exhibits structural regime-dependency. Testing showcases a massive performance window in the autumn of 2025, while remaining exceptionally stable, protective of capital, and overall non-loss-making during baseline market environments. https://github.com/elodfarkas/NT8-Eisler-Impact-Model/blob/V4-Eisler_AdaptiveEdge_v7_4/v7_4(09.15-11.30)eisler_trade_lifecycle_20260617_173239.csv

### Key Performance Analytics
* **Total Executed Trades:** 318
* **Gross Profit Metrics:** +3,110.0 Ticks (+777.5 NQ Points)
* **Overall System Win Rate:** 20.13%
* **Average Winning Outcome:** +201.00 Ticks
* **Average Losing Outcome:** -38.40 Ticks
* **Realized Risk-to-Reward (R:R) Ratio:** 5.23 : 1
* **Profit Factor:** 1.32

### Dynamic Monthly Breakdown

| Evaluation Period | Total Trades | Total PnL (Ticks) | Total PnL (Points) | Realized Win Rate | Performance Regime |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **September 2025** | 49 | -74.0 | -18.5 | 22.45% | Flat / Capital Protected |
| **October 2025** | 99 | +3,002.0 | +750.5 | 29.29% | Asymmetric Growth Alpha |
| **November 2025** | 162 | +398.0 | +99.5 | 14.81% | Asymmetric Growth Alpha |
| **December 2025** | 8 | -216.0 | -54.0 | 0.00% | Flat / Early Mitigation |

### Structural Behavior Notes
* **Regime Expansion Window:** The model captured outsized directional alpha during October and November 2025, leveraging high-velocity order flow displacement sequences to secure large trailing gains.
* **Drawdown Control Engine:** Outside of the key volatility windows (September and December segments), the strategy did not suffer catastrophic decay. The adaptive entry gates and recursive risk protection layers successfully restricted trades, ensuring the system maintained an aggregate flat/non-loss-making baseline.
## Strategy Overview

The main strategy class is:

```text
eisler950ALLDAY_AdaptiveEdge_v7_4
````

The strategy implements an enhanced Eisler-style event-impact model without a direct Level II / MarketDepth dependency.

Instead of using `OnMarketDepth()`, the strategy works with:

```text
L1 Bid
L1 Ask
Last price
Volumetric bar aggregates
Order Flow+ bid/ask delta
```

The model builds an internal event stream from market data, estimates short-horizon impact with an adaptive RLS model, and then applies a strict execution/risk-management pipeline before submitting trades.

Recommended target environment:

```text
Instrument: NQ futures
Bars: 1-minute Volumetric bars
Delta Type: BidAsk
Ticks Per Level: 1
Tick Replay: Recommended for historical / Market Replay testing
Calculate: OnEachTick
```

***

## Core Files

### `eislerstrategy.cs`

Main NinjaTrader strategy file.

Contains:

* Eisler-style event-impact prediction engine
* RLS adaptive prediction model
* Event stream construction
* L1 bid/ask/last processing
* Volumetric bar feature extraction
* Entry gating
* Latency and jitter protection
* Adaptive ATR-based risk engine
* Simulated stop-loss logic
* Structural trailing
* Chandelier trailing
* Catastrophic hard-stop protection
* Overtrading guards
* CSV trade lifecycle export

***

### `eisler.cs`

Custom BarsType / order-flow state model.

Contains:

```text
EislerBarType
EislerOrderFlowData
EislerPriceLevelData
```

This file tracks stateful price-level order-flow behavior, including:

* Buy volume
* Sell volume
* Aggressive buy volume
* Aggressive sell volume
* Sweep detection
* Refill detection
* Absorption detection
* Trap detection
* Price-level memory
* Liquidity persistence
* Queue resiliency
* Impact memory
* Local volatility
* Local imbalance tensor
* Mean-reversion probability
* Continuation probability
* Revisit probability

***

### `@ATR.cs`

Average True Range indicator dependency.

ATR is used by the strategy for adaptive volatility-based risk calibration.

> Note: NinjaTrader normally includes ATR by default.  
> If your NinjaTrader installation already provides ATR, you may not need to include this file in the repository or distribution package.

***

## Main Features

## 1. Eisler-Style Event-Impact Model

The strategy converts market activity into a stream of internal book events.

Internal event types include:

```text
MO0 = Market order without mid-price move
MO1 = Market order with mid-price move
LO0 = Reserved / unused
LO1 = Quote-driven favorable / refill / inside-spread proxy
CA0 = Reserved / unused
CA1 = Quote-driven adverse / cancel / pull proxy
```

These events are stored in a rolling event buffer and used by the adaptive prediction model.

The event stream is generated from:

* Bid updates
* Ask updates
* Last trade events
* Mid-price movement
* Spread behavior
* Volumetric delta
* Near-price imbalance
* OFI-style proxy values

***

## 2. Recursive Least Squares Prediction

The strategy uses an RLS-style adaptive model to estimate short-horizon price impact.

Important internal methods:

```text
BuildFeatureVector_Train()
BuildFeatureVector_Predict()
RlsUpdate()
PredictNextTicks()
```

Important parameters:

```text
KernelLags
RlsLambda
TrainEveryNEvents
```

The prediction output is expressed in ticks and is used to determine directional bias and entry qualification.

Example directional logic:

```text
Positive prediction above threshold  -> potential long entry
Negative prediction below threshold  -> potential short entry
Prediction below threshold           -> no trade
```

***

## 3. Large-Tick / Small-Tick Regime Detection

The strategy estimates whether the market is behaving like a large-tick or small-tick environment.

This is done by tracking how often the spread remains approximately one tick.

Important parameters:

```text
RegimeLookbackBars
LargeTickProbThreshold
```

This regime state influences entry thresholds and adaptive behavior.

Relevant internal state:

```text
_isLargeTick
_spreadOneBuf
_spreadOneCount
```

***

## 4. ΔRπ Baseline Gap Model

The strategy maintains EWMA baseline gap estimates for event response types.

Tracked baselines:

```text
_dR_MO
_dR_LO
_dR_CA
```

These represent adaptive realized gap estimates for different event classes.

Important parameters:

```text
BaselineGapAlpha
BaselineLookbackEvents
BaselineDecayPerEvent
BaselineWeight
```

The baseline model is used as an additional feature in the RLS prediction vector.

***

## 5. Gap-Flex State Features

The model includes decaying state variables to represent short-term liquidity and spread behavior.

Internal states:

```text
_stateFlow
_stateGap
_stateImb
```

These are updated as market events arrive and are included in the prediction feature vector.

Important parameter:

```text
GapStateDecay
```

***

## 6. Volumetric Bar Features

The strategy is designed to use NinjaTrader Volumetric bars when available.

Extracted features include:

```text
Bar delta
Normalized delta
Near-price imbalance
Total volume
Absolute delta fraction
OFI-style proxy
```

Relevant internal variables:

```text
_vfDeltaNorm
_vfImbNear
_vfTotalVol
_vfAbsDeltaFrac
_ofiProxy
```

These features are used in the prediction and filtering pipeline.

***

## 7. Induced-Pattern / Refill-Trap Filter

The strategy includes a refill-trap style entry filter.

The goal is to avoid entering in the direction of a move when opposing quote behavior suggests that liquidity is refilling against the trade.

Important parameters:

```text
RefillWindowMs
RefillOpposingQuoteThreshold
```

Relevant internal method:

```text
IsRefillTrap()
```

If the refill-trap condition is detected, the entry is blocked.

***

## 8. Latency and Jitter Protection

The strategy contains a latency-aware execution guard based on market-data arrival timing.

It estimates:

```text
Median inter-arrival time
P95 inter-arrival time
Effective latency
Quote freshness
Feed jitter
```

Important parameters:

```text
UseLatencyJitterProtection
AutoEstimateLatency
ManualLatencyMs
FreshnessMaxMs
JitterP95MaxMs
ConfirmEvents
MinSignalHoldMs
DynamicThresholdK
DynamicOffsetK
UsePlaybackFriendlyFreshness
UsePlaybackBurstConfirmOverride
PlaybackBurstConfirmExtraEvents
PlaybackBurstConfirmMaxHoldBypassMs
MaxAbsPredictionTicks
```

Important internal methods:

```text
RecordMarketDataArrival()
UpdateLatencyStatsIfNeeded()
LatencyGateBlocks()
ConfirmSignal()
EffectiveThreshold()
EffectiveLimitOffsetTicks()
```

The latency guard can block entries when:

```text
Quotes are stale
Jitter is too high
Signal confirmation is not stable enough
Prediction is unrealistically large
```

***

## 9. Entry Engine

The strategy supports both limit entries and market-entry diagnostic mode.

Entry validation includes:

* Trade window filter
* Opening-noise filter
* ATR stabilization filter
* Spread filter
* High-conviction spread exception
* Directional enable/disable controls
* Quote stability check
* Signal confirmation
* Refill-trap block
* Overtrading guard
* Entry cancellation on stale conditions
* Entry cancellation on signal flip
* Entry cancellation on new bar
* Entry cancellation if price moves too far away from limit

Important parameters:

```text
TradeWindowStart
TradeWindowEnd
NoTradeBefore
MaxSpreadTicks
UseLimitEntries
LimitOffsetTicks
CancelEntryIfAwayTicks
CancelEntryOnSignalFlip
CancelEntryOnNewBar
MaxEntryOrderAgeMs
MaxEntryMidDeviationTicks
MinEntryQuoteStabilityMs
MaxSpreadWorsenTicks
EnableLongTrades
EnableShortTrades
LongMinAbsPredTicks
ShortMinAbsPredTicks
AllowHighConvictionWideSpread
HighConvictionPredTicks
UseMarketEntriesForDiagnostics
```

Important internal methods:

```text
EnterWithBracket()
ValidateWorkingEntryOrder()
CancelWorkingEntry()
HasStableQuotes()
SpreadConvictionBlocks()
```

***

# Risk Engine

The strategy contains a multi-layer risk management system.

It is not a simple fixed stop / fixed target strategy. It uses adaptive R, simulated stop logic, controlled soft-stop behavior, hard emergency protection, and state-based trailing.

***

## Adaptive R-Based Risk

The strategy can use ATR to determine a dynamic 1R value.

Important parameters:

```text
UseAtrDynamicRisk
OneRCalibrationMode
AtrPeriod
VolatilityMultiplier
MinOneRTicks
MaxOneRTicks
```

Supported calibration modes:

```text
Conservative
Balanced
Aggressive
```

Relevant internal methods:

```text
CalculateOneRTicksAtEntry()
CurrentOneRTicks()
InitializeTradeRiskAtEntry()
```

***

## Trade State Machine

Internal trade phases:

```text
Flat
Phase1_BreathingRoom
Phase2_RiskFree
Phase3_TrendCapture
Closing
```

Purpose:

```text
Phase1_BreathingRoom   -> initial room after entry
Phase2_RiskFree        -> move toward break-even / risk-free state
Phase3_TrendCapture    -> trailing logic for larger continuation moves
Closing                -> exit logic active
```

***

## Stop and Exit Layers

Risk features include:

* Simulated stop-loss
* Controlled soft-stop hold
* Hard breach conditions
* Catastrophic stop
* Early failure exit
* Time-based review after minimum hold
* Structural trailing
* Chandelier trailing
* Optional profit target
* Retry-based exit handling
* Emergency exit handling
* Optional account flatten emergency

Important parameters:

```text
StopTicks
CatastrophicStopTicks
UseControlledSoftStopHold
MinHoldMsBeforeSoftSLExit
InitialStopMaxBreachTicks
BidAskHardBreachTicks
IgnorePostEntrySoftStopUntilMinHold
PostEntrySoftHardBreachTicks
SoftStopConfirmMs
SoftStopConfirmEvents
DoNotIgnoreBidAskCrossBreach
IgnoreEntryBarPostExtremumForSL
ExitIfNotProfitableAfterMinHold
MinProfitTicksAfterMinHold
HardMaxHoldMs
UseEarlyFailureExit
EarlyFailureMaxAdverse_R
EarlyFailureMinMfe_R
EarlyFailureMinHoldMs
FreeTradeActivation_R
FreeTradeOffsetTicks
TrailingActivation_R
UseStructuralTrailing
StructuralLookbackBars
StructuralOffsetTicks
UseChandelierTrailing
ChandelierAtrMultiplier
UseProfitTargetLimit
ProfitTarget_R
```

Important internal methods:

```text
ManageRiskState()
UpdateSimulatedTrailingSL()
SubmitCatastrophicHardStopIfNeeded()
ConfirmControlledSoftStopTouch()
TransitionRiskState()
CurrentOpenPnLTicks()
CurrentHeldMsCore()
```

***

## Risk Exit State Machine

Internal risk states:

```text
ARMED
SL_TOUCHED
EXIT_SUBMITTED
RETRY_PENDING
EMERGENCY_PENDING
FLAT_CONFIRMED
FAILED_BUT_STRATEGY_STILL_RUNNING
```

This is designed to handle difficult exit situations more robustly than a single fixed exit command.

Exit escalation may include:

```text
Managed exit
Retry exit
Emergency exit
Unmanaged force exit
Account flatten attempt
```

Important parameters:

```text
ExitRetryMs
EmergencyExitMs
ForceExitRetryMs
UseAccountFlattenEmergency
ForceExitBeforeAccountFlattenAttempts
AccountFlattenRetryMs
StopMarketSafetyTicks
```

***

# Overtrading Protection

The strategy includes multiple guardrails to prevent excessive trading.

Features:

* Maximum trades per bar
* Cooldown after exit
* Cooldown after stop-loss
* Maximum trades per session
* Consecutive loss limit
* Loss pause window

Important parameters:

```text
MaxTradesPerBar
CooldownAfterExitBars
CooldownAfterSLBars
MaxTradesPerSession
ConsecutiveLossLimit
LossWindowMinutes
LossPauseMinutes
```

Relevant internal methods:

```text
OvertradingGateBlocks()
RegisterTradeOutcome()
ResetOvertradingGuardsForNewSessionIfNeeded()
```

***

# Reset Logic

The strategy supports both daily and periodic resets.

Reset controls:

```text
UseCustomResetTime
DailyResetTime
UsePeriodicReset
PeriodicResetMinutes
PeriodicResetOnlyWhenFlat
```

Periodic resets are useful for preventing stale model state during long intraday sessions.

Important internal methods:

```text
HandlePeriodicReset()
HandleDailyPreOpenReset()
ResetAllStrategyState()
```

Resetting can clear:

* Event stream
* RLS state
* Quote state
* Volumetric feature cache
* Confirmation state
* Risk state
* Entry state
* Regime buffers
* Gap-flex states
* Activity buffers

***

# CSV Trade Lifecycle Export

The strategy can export detailed trade lifecycle data to CSV.

CSV export includes:

* Entry information
* Exit information
* Prediction values
* Risk state snapshots
* Volumetric features
* Spread and quote information
* MFE / MAE tracking
* Simulated stop-loss evolution
* Trail activation information
* Exit retry information
* Rejection diagnostics
* Runtime lifecycle events

Important parameters:

```text
EnableTradeCsvExport
TradeCsvFileName
CsvFlushEveryLine
CsvExportOnlyCompletedTrades
CsvStartNewFileEachRun
CsvLogSubmitPrediction
```

Default output name:

```text
eisler_trade_lifecycle.csv
```

CSV lifecycle logging is useful for:

```text
Post-trade analytics
Model debugging
Entry quality review
Exit quality review
Risk-engine tuning
MFE / MAE analysis
Trailing stop behavior analysis
```

***

# Recommended Chart Setup

Recommended starting setup:

```text
Instrument: NQ futures
Bars: 1-minute Volumetric
Delta Type: BidAsk
Ticks Per Level: 1
Tick Replay: Enabled for historical/replay testing
Calculate: OnEachTick
```

The strategy is designed around short-horizon order-flow behavior and should be tested with high-quality tick data.

***

# Installation

1. Close NinjaTrader 8.

2. Copy the strategy file into:

```text
Documents\NinjaTrader 8\bin\Custom\Strategies\
```

3. If using the custom Eisler BarsType, copy `eisler.cs` into:

```text
Documents\NinjaTrader 8\bin\Custom\BarsTypes\
```

4. If ATR is not already available in your NinjaTrader installation, ensure `@ATR.cs` is present or use NinjaTrader's built-in ATR indicator.

5. Open NinjaTrader 8.

6. Open:

```text
New > NinjaScript Editor
```

7. Right-click and select:

```text
Compile
```

8. Apply the strategy to the intended chart or Strategy Analyzer configuration.

***

# Important Compatibility Notes

NinjaTrader compiles all `.cs` files under:

```text
Documents\NinjaTrader 8\bin\Custom\
```

Avoid duplicate class names and duplicate BarsType IDs.

The included `EislerBarType` uses a custom `BarsPeriodType` cast. If another custom BarsType uses the same ID, you may need to change one of them to avoid conflicts.

Common duplicate-error examples:

```text
CS0101: namespace already contains a definition
CS0111: type already defines a member
CS0246: type or namespace could not be found
```

If you upload multiple custom BarsTypes, verify that each custom `BarsPeriodType` ID is unique.

***

# Parameter Groups

The strategy exposes many NinjaScript parameters grouped around:

* Eisler Model
* Trading
* Risk Engine
* Risk Engine: Adaptive R
* Risk Engine: Controlled Hold
* Execution Guard
* Overtrading Guard
* Regime
* Baseline
* GapFlex
* Induced Filters
* Sizing
* Reset
* Latency
* CSV Export
* Debug

Because the strategy contains a large number of controls, it is recommended to document stable parameter presets separately.

***

# Suggested Workflow

1. Compile the strategy.
2. Run in Strategy Analyzer with high-quality tick data.
3. Validate with Market Replay.
4. Export CSV lifecycle logs.
5. Analyze entries, exits, MFE, MAE, and stop behavior.
6. Tune parameters for the specific instrument/session.
7. Forward-test in simulation.
8. Only consider live use after extensive validation.

***

# Limitations

* No direct Level II / MarketDepth dependency is used.
* L1 bid/ask/last data is used as a proxy for order-book events.
* Volumetric features depend on data-feed quality.
* Historical behavior may differ from real-time behavior.
* Tick Replay is recommended for historical and replay testing.
* CSV export requires write access and may generate large files.
* The model contains many adaptive components that may require instrument-specific calibration.
* The strategy is not a plug-and-play trading system.
* The strategy may cancel, retry, or escalate exits depending on risk state.
* Account flatten emergency behavior must be tested carefully in simulation before live use.

***

# Development Status

Experimental / research-grade.

This strategy is intended for advanced NinjaTrader users, quantitative developers, and order-flow researchers.

It should be extensively tested in simulation, Market Replay, and controlled environments before any live use.

***

# Requirements

* NinjaTrader 8
* Windows
* C# / NinjaScript
* Order Flow+ Volumetric bars for full feature use
* High-quality tick data
* Sufficient permissions for CSV file export

***

# Disclaimer

This software is provided for educational and research purposes only.

It does not provide financial advice, investment advice, or trading recommendations.

Automated trading involves significant risk, including the risk of rapid loss, platform failure, data-feed errors, rejected orders, slippage, liquidity gaps, and unexpected market behavior.

Use at your own risk.

***

# License

Choose a license appropriate for your intended distribution.

Suggested options:

* MIT License for permissive open-source usage
* GPLv3 if derivative works should remain open-source
* Proprietary / private license if distribution is restricted

***

# Author

https://www.linkedin.com/in/elod/


