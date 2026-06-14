# NT8-Eisler-Impact-Model
Advanced event-impact algorithmic trading strategy for NinjaTrader 8. Features dynamic regime switching, recursive least squares (RLS) gap correction, and latency-protected automated execution using C# and NQ volumetric order flow.

Files:
V1:eisler950.cs
V2:EislerCSVdata.zip

# NinjaTrader 8: Enhanced Eisler Event-Impact Strategy

This repository contains an advanced algorithmic trading strategy for NinjaTrader 8. It implements an enhanced Eisler-style event-impact model designed for high-frequency data environments, operating entirely without Level II (MarketDepth) dependencies. 

The strategy is optimized for NQ (Nasdaq 100) 1-minute Volumetric bars (Order Flow+) utilizing Level 1 Bid/Ask/Last dynamics.

## Core Enhancements
This model introduces several structural improvements to standard event-impact trading logic:
1. Regime Switching: Dynamically switches between Large-tick and Small-tick regimes based on spread-one probabilities.
2. Baseline ΔRπ (EWMA): Tracks the realized gap per price-changing event type using an Exponentially Weighted Moving Average.
3. Gap-Flex Correction: Utilizes a Recursive Least Squares (RLS) feature set with three decaying states to correct small-tick variances.
4. Induced-Pattern Filters: Features a refill-trap gate to filter out deceptive order book refills and prevent poor entries.
5. Concave Impact Sizing: Dynamically adjusts position sizing based on a volumetric activity proxy.

## Robustness & Execution
To survive in live market conditions, the script includes built-in latency and jitter protection:
Asynchronous Data Handling: Tracks market data inter-arrival times to calculate effective latency and P95 jitter metrics.
Dynamic Gating: Automatically blocks entries if the data feed becomes stale or if quote jitter exceeds acceptable thresholds.
Signal Confirmation: Requires multi-tick signal stability to prevent execution on micro-second phantom spikes.

## Setup Requirements
Platform: NinjaTrader 8
Data Feed: Tick Replay MUST be enabled for historical backtesting and Market Replay.
Bar Type: 1-minute Volumetric Bars (DeltaType = BidAsk, TicksPerLevel = 1).

## References & Academic Foundation
The core logic of this strategy is heavily inspired by the quantitative research on market microstructure, specifically the analysis of how order book events (market orders, limit orders, and cancellations) impact prices.
Primary Literature: Eisler, Z., Bouchaud, J.-P., & Kockelkoren, J. (2010). *The price impact of order book events: market orders, limit orders and cancellations*. Quantitative Finance. [arXiv:0904.0900](https://arxiv.org/abs/0904.0900)
