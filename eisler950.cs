// eisler950.cs
// NinjaTrader 8 Strategy
// Enhanced Eisler-style event-impact model WITHOUT Level II (MarketDepth) dependency.
// Target: NQ 1-minute Volumetric bars (Order Flow+), DeltaType=BidAsk, TicksPerLevel=1.
//
// Enhancements implemented (priority list):
//  1) Large-tick vs Small-tick regime switch (spread-one probability)
//  2) ΔRπ baseline (EWMA realized gap per price-changing event type)
//  3) Small-tick gap-flex correction via 3 decaying states included in RLS feature set
//  4) Induced-pattern filters (refill-trap gate for entries)
//  5) Concave impact-based dynamic sizing (activity proxy)
//
// Notes:
//  - No OnMarketDepth() used. Uses L1 Bid/Ask/Last + Volumetric bar aggregates.
//  - For historical/Market Replay, Tick Replay ON recommended.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.BarsTypes;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class eisler950ALLDAY : Strategy
	{
		private enum EventType
		{
			MO0 = 0,   // market order, no mid move within window
			MO1 = 1,   // market order, mid moved within window
			LO0 = 2,   // unused (reserved)
			LO1 = 3,   // quote-driven: best quote moved (proxy for LO'/refill/inside-spread)
			CA0 = 4,   // unused (reserved)
			CA1 = 5    // quote-driven: best quote moved adverse (proxy for CA'/pull)
		}

		private struct BookEvent
		{
			public DateTime Time;
			public EventType Type;
			public int Eps;
			public double Mid;
			public double MidChangeTicks;
			public double SpreadTicks;
			public double DeltaNorm;
			public double ImbNear;
			public double OfiProxy;
			public bool PriceChanged;
		}

		// -------------------- User parameters --------------------
		[NinjaScriptProperty]
		[Range(30, 600)]
		[Display(Name = "KernelLags", Order = 1, GroupName = "Eisler Model")]
		public int KernelLags { get; set; }

		[NinjaScriptProperty]
		[Range(0.90, 0.9999)]
		[Display(Name = "RlsLambda (forgetting)", Order = 2, GroupName = "Eisler Model")]
		public double RlsLambda { get; set; }

		[NinjaScriptProperty]
		[Range(1, 15)]
		[Display(Name = "EntryThresholdTicks (LargeTick)", Order = 3, GroupName = "Trading")]
		public int EntryThresholdTicksLarge { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "EntryThresholdTicks (SmallTick)", Order = 4, GroupName = "Trading")]
		public int EntryThresholdTicksSmall { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "StopTicks", Order = 5, GroupName = "Trading")]
		public int StopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 80)]
		[Display(Name = "TargetTicks", Order = 6, GroupName = "Trading")]
		public int TargetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "MaxSpreadTicks", Order = 7, GroupName = "Trading")]
		public int MaxSpreadTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TradeWindowStart (HH:mm)", Order = 8, GroupName = "Trading")]
		public string TradeWindowStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TradeWindowEnd (HH:mm)", Order = 9, GroupName = "Trading")]
		public string TradeWindowEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseLimitEntries", Order = 10, GroupName = "Trading")]
		public bool UseLimitEntries { get; set; }

		[NinjaScriptProperty]
		[Range(0, 6)]
		[Display(Name = "LimitOffsetTicks", Order = 11, GroupName = "Trading")]
		public int LimitOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 30)]
		[Display(Name = "CancelEntryIfAwayTicks", Order = 12, GroupName = "Trading")]
		public int CancelEntryIfAwayTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseQuoteEvents (LO'/CA' proxies)", Order = 13, GroupName = "Eisler Model")]
		public bool UseQuoteEvents { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "QuoteEventMinMs", Order = 14, GroupName = "Eisler Model")]
		public int QuoteEventMinMs { get; set; }

		[NinjaScriptProperty]
		[Range(1, 12)]
		[Display(Name = "ImbLookbackTicks", Order = 15, GroupName = "Order Flow+")]
		public int ImbLookbackTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "MinTradeSize", Order = 16, GroupName = "Order Flow+")]
		public int MinTradeSize { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "TrainEveryNEvents", Order = 17, GroupName = "Eisler Model")]
		public int TrainEveryNEvents { get; set; }

		// --- 1) Regime switch params ---
		[NinjaScriptProperty]
		[Range(10, 300)]
		[Display(Name = "RegimeLookbackBars", Order = 18, GroupName = "Regime")]
		public int RegimeLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(0.50, 0.99)]
		[Display(Name = "LargeTickProbThreshold", Order = 19, GroupName = "Regime")]
		public double LargeTickProbThreshold { get; set; }

		// --- 2) ΔRπ baseline params ---
		[NinjaScriptProperty]
		[Range(0.001, 0.50)]
		[Display(Name = "BaselineGapAlpha (EWMA)", Order = 20, GroupName = "Baseline")]
		public double BaselineGapAlpha { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "BaselineLookbackEvents", Order = 21, GroupName = "Baseline")]
		public int BaselineLookbackEvents { get; set; }

		[NinjaScriptProperty]
		[Range(0.80, 0.999)]
		[Display(Name = "BaselineDecayPerEvent", Order = 22, GroupName = "Baseline")]
		public double BaselineDecayPerEvent { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 3.0)]
		[Display(Name = "BaselineWeight", Order = 23, GroupName = "Baseline")]
		public double BaselineWeight { get; set; }

		// --- 3) Gap-flex states (decay) ---
		[NinjaScriptProperty]
		[Range(0.80, 0.999)]
		[Display(Name = "GapStateDecay", Order = 24, GroupName = "GapFlex")]
		public double GapStateDecay { get; set; }

		// --- 4) Induced-pattern filter params ---
		[NinjaScriptProperty]
		[Range(50, 5000)]
		[Display(Name = "RefillWindowMs", Order = 25, GroupName = "Induced Filters")]
		public int RefillWindowMs { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "RefillOpposingQuoteThreshold", Order = 26, GroupName = "Induced Filters")]
		public int RefillOpposingQuoteThreshold { get; set; }

		// --- 5) Concave impact sizing params ---
		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "BaseQuantity", Order = 27, GroupName = "Sizing")]
		public int BaseQuantity { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 10.0)]
		[Display(Name = "SizeActivityK", Order = 28, GroupName = "Sizing")]
		public double SizeActivityK { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "ActivityLookbackBars", Order = 29, GroupName = "Sizing")]
		public int ActivityLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DebugPrint", Order = 30, GroupName = "Debug")]
		public bool DebugPrint { get; set; }

		// -------------------- Reset parameters --------------------
		[NinjaScriptProperty]
		[Display(Name = "UseCustomResetTime", Order = 31, GroupName = "Reset")]
		public bool UseCustomResetTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DailyResetTime (HH:mm)", Order = 32, GroupName = "Reset")]
		public string DailyResetTime { get; set; }

		// NEW: Periodic reset every N minutes (clock-multiple)
		[NinjaScriptProperty]
		[Display(Name = "UsePeriodicReset", Order = 33, GroupName = "Reset")]
		public bool UsePeriodicReset { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "PeriodicResetMinutes", Order = 34, GroupName = "Reset")]
		public int PeriodicResetMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PeriodicResetOnlyWhenFlat", Order = 35, GroupName = "Reset")]
		public bool PeriodicResetOnlyWhenFlat { get; set; }



// -------------------- Latency / Jitter robustness --------------------
[NinjaScriptProperty]
[Display(Name = "UseLatencyJitterProtection", Order = 40, GroupName = "Latency")]
public bool UseLatencyJitterProtection { get; set; }

[NinjaScriptProperty]
[Display(Name = "AutoEstimateLatency", Order = 41, GroupName = "Latency")]
public bool AutoEstimateLatency { get; set; }

[NinjaScriptProperty]
[Range(0, 50)]
[Display(Name = "ManualLatencyMs (CQG ~2ms)", Order = 42, GroupName = "Latency")]
public int ManualLatencyMs { get; set; }

[NinjaScriptProperty]
[Range(10, 500)]
[Display(Name = "FreshnessMaxMs", Order = 43, GroupName = "Latency")]
public int FreshnessMaxMs { get; set; }

[NinjaScriptProperty]
[Range(5, 500)]
[Display(Name = "JitterP95MaxMs", Order = 44, GroupName = "Latency")]
public int JitterP95MaxMs { get; set; }

[NinjaScriptProperty]
[Range(50, 4000)]
[Display(Name = "MaxEntryOrderAgeMs", Order = 45, GroupName = "Latency")]
public int MaxEntryOrderAgeMs { get; set; }

[NinjaScriptProperty]
[Range(0, 2000)]
[Display(Name = "MinOrderWorkingMs", Order = 46, GroupName = "Latency")]
public int MinOrderWorkingMs { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "ConfirmEvents", Order = 47, GroupName = "Latency")]
public int ConfirmEvents { get; set; }

[NinjaScriptProperty]
[Range(0, 2000)]
[Display(Name = "MinSignalHoldMs", Order = 48, GroupName = "Latency")]
public int MinSignalHoldMs { get; set; }

[NinjaScriptProperty]
[Range(0.0, 5.0)]
[Display(Name = "DynamicThresholdK", Order = 49, GroupName = "Latency")]
public double DynamicThresholdK { get; set; }

[NinjaScriptProperty]
[Range(0.0, 5.0)]
[Display(Name = "DynamicOffsetK", Order = 50, GroupName = "Latency")]
public double DynamicOffsetK { get; set; }
		// -------------------- L1 quote state --------------------
		private double _bestBid = double.NaN;
		private double _bestAsk = double.NaN;
		private double _prevMid = double.NaN;

		// Pending trade -> decide MO0 vs MO1
		private bool _pendingTrade = false;
		private DateTime _pendingTradeTime = Core.Globals.MinDate;
		private int _pendingSide = 0;
		private double _pendingMid = double.NaN;
		private int _pendingWindowMs = 120;

		private DateTime _lastQuoteEventTime = Core.Globals.MinDate;

		// Induced-pattern tracking (refill trap)
		private DateTime _lastPriceMoveTime = Core.Globals.MinDate;
		private int _lastPriceMoveDir = 0; // +1 up, -1 down
		private int _oppQuoteCount = 0;

		private readonly LinkedList<BookEvent> _events = new LinkedList<BookEvent>();
		private int _eventCounter = 0;

		private Order _entryOrder = null;
		private double _entryLimitPx = 0;
		private TimeSpan _twStart, _twEnd;

		// --- Daily pre-open reset tracking ---
		private DateTime _lastDailyResetDate = Core.Globals.MinDate;
		private TimeSpan _resetTime;
		private bool _resetTimeParsed = false;

		// --- Periodic reset tracking ---
		private DateTime _lastPeriodicResetKey = Core.Globals.MinDate;


// --- Latency / jitter tracking (arrival-time based proxies) ---
private DateTime _lastMdArriveTime = Core.Globals.MinDate;
private DateTime _lastBidAskArriveTime = Core.Globals.MinDate;
private double[] _mdInterArrivalMs;
private int _mdIaIdx = 0;
private int _mdIaCount = 0;
private double _jitterP95Ms = 0.0;
private double _jitterMedianMs = 0.0;
private double _effectiveLatencyMs = 0.0;
private DateTime _confirmStartTime = Core.Globals.MinDate;
private int _confirmDir = 0;
private int _confirmCount = 0;
private double _lastPred = 0.0;
private int _lastPredDir = 0;
private DateTime _entrySubmitLocalTime = Core.Globals.MinDate;
private DateTime _entryWorkingLocalTime = Core.Globals.MinDate;
		// Volumetric
		private VolumetricBarsType _volBars = null;
		private bool _hasVolumetric = false;
		private int _vfBar = -1;
		private double _vfDeltaNorm = 0.0;
		private double _vfImbNear = 0.0;
		private double _ofiProxy = 0.0;
		private double _vfTotalVol = 0.0;
		private double _vfAbsDeltaFrac = 0.0;

		// 1) Regime detection buffers
		private int[] _spreadOneBuf;
		private int _spreadOneIdx = 0;
		private int _spreadOneCount = 0;
		private bool _isLargeTick = true;

		// 2) Baseline ΔRπ (EWMA realized gaps) in ticks
		private double _dR_MO = 1.0;
		private double _dR_LO = 1.0;
		private double _dR_CA = 1.0;

		// 3) Gap-flex decaying states (small tick focused)
		private double _stateFlow = 0.0;
		private double _stateGap = 0.0;
		private double _stateImb = 0.0;
		private double _avgSpreadEwma = 1.0;

		// 5) Activity buffers (for concave sizing)
		private double[] _barVolBuf;
		private int _barVolIdx = 0;
		private double _barVolSum = 0.0;

		// RLS
		private int _featDim;
		private int _baseDim;
		private double[] _w;
		private double[,] _P;
		private double[] _x;
		private double[] _Px;
		private double[] _k;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "eisler950";
				Description = "Enhanced Eisler-style event-impact WITHOUT Level II, tuned for NQ 1-minute Volumetric (Bid/Ask) using L1 Bid/Ask/Last. Adds regime switch, ΔR baseline, gap-flex states, induced filters, concave sizing.";
				Calculate = Calculate.OnEachTick;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsOverlay = true;

				KernelLags = 160;
				RlsLambda = 0.995;
				EntryThresholdTicksLarge = 3;
				EntryThresholdTicksSmall = 4;
				StopTicks = 14;
				TargetTicks = 10;
				MaxSpreadTicks = 2;
				TradeWindowStart = "09:50";
				TradeWindowEnd = "11:30";
				UseLimitEntries = true;
				LimitOffsetTicks = 1;
				CancelEntryIfAwayTicks = 6;

				UseQuoteEvents = true;
				QuoteEventMinMs = 5;
				ImbLookbackTicks = 3;
				MinTradeSize = 1;
				TrainEveryNEvents = 1;

				RegimeLookbackBars = 120;
				LargeTickProbThreshold = 0.85;

				BaselineGapAlpha = 0.03;
				BaselineLookbackEvents = 120;
				BaselineDecayPerEvent = 0.985;
				BaselineWeight = 0.6;

				GapStateDecay = 0.97;
				RefillWindowMs = 900;
				RefillOpposingQuoteThreshold = 3;

				BaseQuantity = 1;
				SizeActivityK = 1.2;
				ActivityLookbackBars = 60;

				DebugPrint = false;

				// Reset defaults
				UseCustomResetTime = false;
				DailyResetTime = "09:49";

				// Periodic reset defaults (requested: every 5 minutes)
				UsePeriodicReset = true;
				PeriodicResetMinutes = 5;
				PeriodicResetOnlyWhenFlat = true;

				// Latency defaults (CQG ~2ms, but use robust gating)
				UseLatencyJitterProtection = true;
				AutoEstimateLatency = true;
				ManualLatencyMs = 2;
				FreshnessMaxMs = 75;
				JitterP95MaxMs = 120;
				MaxEntryOrderAgeMs = 900;
				MinOrderWorkingMs = 150;
				ConfirmEvents = 2;
				MinSignalHoldMs = 60;
				DynamicThresholdK = 0.7;
				DynamicOffsetK = 0.6;
			}
			else if (State == State.Configure)
			{
				ParseTradeWindow();
				ParseResetTime();

				_baseDim = 6 * KernelLags;
				// extra features:
				//  0 SpreadTicks
				//  1 vfDeltaNorm
				//  2 vfImbNear
				//  3 ofiProxy
				//  4 isLargeTick (0/1)
				//  5 baselinePred
				//  6 stateFlow
				//  7 stateGap
				//  8 stateImb
				_featDim = _baseDim + 9;

				_w = new double[_featDim];
				_P = new double[_featDim, _featDim];
				_x = new double[_featDim];
				_Px = new double[_featDim];
				_k = new double[_featDim];
				InitRls();

				_spreadOneBuf = new int[Math.Max(10, RegimeLookbackBars)];
				_barVolBuf = new double[Math.Max(10, ActivityLookbackBars)];
				_mdInterArrivalMs = new double[512];
			}
			else if (State == State.DataLoaded)
			{
				try
				{
					_volBars = Bars != null ? (Bars.BarsSeries.BarsType as VolumetricBarsType) : null;
					_hasVolumetric = _volBars != null;
				}
				catch
				{
					_volBars = null;
					_hasVolumetric = false;
				}
			}
		}

		private void ParseTradeWindow()
		{
			if (!TimeSpan.TryParse(TradeWindowStart, out _twStart))
				_twStart = new TimeSpan(9, 30, 0);
			if (!TimeSpan.TryParse(TradeWindowEnd, out _twEnd))
				_twEnd = new TimeSpan(16, 0, 0);
		}

		private void ParseResetTime()
		{
			_resetTimeParsed = TimeSpan.TryParse(DailyResetTime, out _resetTime);
			if (!_resetTimeParsed)
				_resetTime = new TimeSpan(9, 29, 0);
		}

		private bool InTradeWindow(DateTime t)
		{
			var tod = t.TimeOfDay;
			return tod >= _twStart && tod < _twEnd;
		}



// -------------------- Latency / Jitter helpers --------------------
private void RecordMarketDataArrival(DateTime arriveTime, bool isBidAsk)
{
	// inter-arrival (proxy for jitter/buffering)
	if (_lastMdArriveTime != Core.Globals.MinDate)
	{
		double ia = (arriveTime - _lastMdArriveTime).TotalMilliseconds;
		if (ia >= 0 && ia < 10_000)
		{
			if (_mdInterArrivalMs == null || _mdInterArrivalMs.Length == 0)
				_mdInterArrivalMs = new double[512];
			_mdInterArrivalMs[_mdIaIdx] = ia;
			_mdIaIdx = (_mdIaIdx + 1) % _mdInterArrivalMs.Length;
			_mdIaCount = Math.Min(_mdIaCount + 1, _mdInterArrivalMs.Length);
		}
	}
	_lastMdArriveTime = arriveTime;
	if (isBidAsk)
		_lastBidAskArriveTime = arriveTime;
}

private void UpdateLatencyStatsIfNeeded()
{
	if (!UseLatencyJitterProtection) return;
	if (_mdIaCount < 10) return;

	// Copy the most recent samples into a temp array and compute median + p95.
	int n = _mdIaCount;
	double[] tmp = new double[n];
	for (int i = 0; i < n; i++)
	{
		int idx = (_mdIaIdx - 1 - i);
		if (idx < 0) idx += _mdInterArrivalMs.Length;
		tmp[i] = _mdInterArrivalMs[idx];
	}
	Array.Sort(tmp);
	_jitterMedianMs = tmp[n / 2];
	int p95i = (int)Math.Floor(0.95 * (n - 1));
	_jitterP95Ms = tmp[Math.Max(0, Math.Min(n - 1, p95i))];

	// Effective latency: base latency + jitter buffer (p95)
	double baseMs = Math.Max(0, ManualLatencyMs);
	if (AutoEstimateLatency)
	{
		// If feed is smooth, median inter-arrival can be a proxy for minimum practical reaction time.
		baseMs = Math.Max(baseMs, _jitterMedianMs);
	}
	_effectiveLatencyMs = baseMs + _jitterP95Ms;
}

private double CurrentFreshnessMs()
{
	if (_lastBidAskArriveTime == Core.Globals.MinDate) return 999999;
	return (Core.Globals.Now - _lastBidAskArriveTime).TotalMilliseconds;
}

private bool LatencyGateBlocks(out string reason)
{
	reason = null;
	if (!UseLatencyJitterProtection) return false;
	UpdateLatencyStatsIfNeeded();

	double fresh = CurrentFreshnessMs();
	if (fresh > Math.Max(10, FreshnessMaxMs))
	{
		reason = $"Stale quotes (freshness={fresh:F0}ms)";
		return true;
	}
	if (_mdIaCount >= 20 && _jitterP95Ms > Math.Max(5, JitterP95MaxMs))
	{
		reason = $"High jitter (p95={_jitterP95Ms:F1}ms)";
		return true;
	}
	return false;
}

private int EffectiveThreshold(int baseThr)
{
	if (!UseLatencyJitterProtection) return baseThr;
	UpdateLatencyStatsIfNeeded();
	double k = Math.Max(0.0, DynamicThresholdK);
	int add = (int)Math.Ceiling(k * (_effectiveLatencyMs / 10.0));
	return Math.Max(1, baseThr + add);
}

private int EffectiveLimitOffsetTicks(int baseOffset)
{
	if (!UseLatencyJitterProtection) return baseOffset;
	UpdateLatencyStatsIfNeeded();
	double k = Math.Max(0.0, DynamicOffsetK);
	int add = (int)Math.Ceiling(k * (_effectiveLatencyMs / 10.0));
	return Math.Max(0, Math.Min(10, baseOffset + add));
}

private bool ConfirmSignal(int dir)
{
	if (!UseLatencyJitterProtection) return true;
	DateTime now = Core.Globals.Now;

	if (dir == 0)
	{
		_confirmDir = 0;
		_confirmCount = 0;
		_confirmStartTime = Core.Globals.MinDate;
		return false;
	}

	if (dir != _confirmDir)
	{
		_confirmDir = dir;
		_confirmCount = 1;
		_confirmStartTime = now;
	}
	else
	{
		_confirmCount++;
	}

	int needN = Math.Max(1, ConfirmEvents);
	double needMs = Math.Max(0, MinSignalHoldMs);
	double heldMs = (_confirmStartTime == Core.Globals.MinDate) ? 0 : (now - _confirmStartTime).TotalMilliseconds;

	return _confirmCount >= needN && heldMs >= needMs;
}
		// -------------------- Periodic reset (every N minutes) --------------------
		private void HandlePeriodicReset()
		{
			if (!UsePeriodicReset) return;

			int nMin = Math.Max(1, Math.Min(60, PeriodicResetMinutes));
			DateTime t = Time[0];

			// Only trigger on clock multiples: minute % N == 0
			if ((t.Minute % nMin) != 0) return;

			// Optional safety: do not reset while in position
			if (PeriodicResetOnlyWhenFlat && Position != null && Position.MarketPosition != MarketPosition.Flat)
				return;

			// Ensure once per qualifying minute
			DateTime key = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
			if (_lastPeriodicResetKey == key) return;

			ResetAllStrategyState();
			_lastPeriodicResetKey = key;

			if (DebugPrint)
				Print($"[RESET] Periodic reset executed at {t:yyyy-MM-dd HH:mm:ss} (interval={nMin}m, minute={t.Minute:00}).");
		}

		// -------------------- Daily reset (custom time or TradeWindowStart - 1 minute) --------------------
		private void HandleDailyPreOpenReset()
		{
			DateTime today = Time[0].Date;

			// safety: ensure time spans are initialized
			if (_twStart == default(TimeSpan))
				ParseTradeWindow();
			if (!_resetTimeParsed)
				ParseResetTime();

			DateTime resetAt;

			// If user provided a custom reset time, use it; otherwise default to TradeWindowStart - 1 minute
			if (UseCustomResetTime)
				resetAt = today.Add(_resetTime);
			else
				resetAt = today.Add(_twStart).AddMinutes(-1);

			// guard if resetAt rolled into previous day
			if (resetAt.Date < today)
				resetAt = today;

			// daily once
			if (Time[0] >= resetAt && _lastDailyResetDate.Date != today)
			{
				ResetAllStrategyState();
				_lastDailyResetDate = today;

			
			}
		}

		private void ResetAllStrategyState()
		{
			// 1) Cancel working entry (if any)
			try
			{
				if (_entryOrder != null && (_entryOrder.OrderState == OrderState.Working || _entryOrder.OrderState == OrderState.Accepted))
					CancelOrder(_entryOrder);
			}
			catch { }

			_entryOrder = null;
			_entryLimitPx = 0;

			// 2) Event stream + counters
			_events.Clear();
			_eventCounter = 0;

			// 3) L1 / mid state
			_bestBid = double.NaN;
			_bestAsk = double.NaN;
			_prevMid = double.NaN;

			// 4) Pending trade resolver
			_pendingTrade = false;
			_pendingTradeTime = Core.Globals.MinDate;
			_pendingSide = 0;
			_pendingMid = double.NaN;

			_lastQuoteEventTime = Core.Globals.MinDate;

			// 5) Induced-pattern tracking
			_lastPriceMoveTime = Core.Globals.MinDate;
			_lastPriceMoveDir = 0;
			_oppQuoteCount = 0;

			// 6) Volumetric feature cache
			_vfBar = -1;
			_vfDeltaNorm = 0.0;
			_vfImbNear = 0.0;
			_vfTotalVol = 0.0;
			_vfAbsDeltaFrac = 0.0;
			_ofiProxy = 0.0;

			// 7) Regime buffers
			_isLargeTick = true;
			_spreadOneIdx = 0;
			_spreadOneCount = 0;
			if (_spreadOneBuf != null) Array.Clear(_spreadOneBuf, 0, _spreadOneBuf.Length);

			// 8) Baseline ΔRπ
			_dR_MO = 1.0;
			_dR_LO = 1.0;
			_dR_CA = 1.0;

			// 9) Gap-flex states
			_stateFlow = 0.0;
			_stateGap = 0.0;
			_stateImb = 0.0;
			_avgSpreadEwma = 1.0;

			// 10) Activity buffers
			_barVolIdx = 0;
			_barVolSum = 0.0;
			if (_barVolBuf != null) Array.Clear(_barVolBuf, 0, _barVolBuf.Length);

			// 11) RLS reset
			if (_w != null) Array.Clear(_w, 0, _w.Length);

			if (_P != null)
			{
				Array.Clear(_P, 0, _P.Length);
				InitRls();
			}

			if (_x != null) Array.Clear(_x, 0, _x.Length);
			if (_Px != null) Array.Clear(_Px, 0, _Px.Length);
			if (_k != null) Array.Clear(_k, 0, _k.Length);
		}

		private void InitRls()
		{
			for (int i = 0; i < _featDim; i++)
				_P[i, i] = 1000.0;
		}

		private double Mid()
		{
			if (double.IsNaN(_bestBid) || double.IsNaN(_bestAsk) || _bestBid <= 0 || _bestAsk <= 0)
				return double.NaN;
			return 0.5 * (_bestBid + _bestAsk);
		}

		private double SpreadTicks()
		{
			if (double.IsNaN(_bestBid) || double.IsNaN(_bestAsk) || _bestBid <= 0 || _bestAsk <= 0)
				return 999;
			return (_bestAsk - _bestBid) / TickSize;
		}

		private void UpdateVolumetricFeaturesIfNeeded()
		{
			if (_vfBar == CurrentBar) return;
			_vfBar = CurrentBar;
			_vfDeltaNorm = 0.0;
			_vfImbNear = 0.0;
			_vfTotalVol = 0.0;
			_vfAbsDeltaFrac = 0.0;

			if (!_hasVolumetric || _volBars == null) return;

			try
			{
				var v = _volBars.Volumes[CurrentBar];
				double total = Math.Max(1.0, (double)v.TotalVolume);
				_vfTotalVol = total;
				_vfDeltaNorm = Math.Max(-1.0, Math.Min(1.0, (double)v.BarDelta / total));
				_vfAbsDeltaFrac = Math.Min(1.0, Math.Abs((double)v.BarDelta) / total);

				int ticks = Math.Max(1, Math.Min(20, ImbLookbackTicks));
				double center = Close[0];
				double sumDelta = 0.0;
				double sumVol = 0.0;
				for (int k = -ticks; k <= ticks; k++)
				{
					double px = Math.Round((center + k * TickSize) / TickSize) * TickSize;
					long a = v.GetAskVolumeForPrice(px);
					long b = v.GetBidVolumeForPrice(px);
					sumDelta += (double)(a - b);
					sumVol += (double)(a + b);
				}
				_vfImbNear = (sumVol > 1.0) ? Math.Max(-1.0, Math.Min(1.0, sumDelta / sumVol)) : 0.0;
			}
			catch
			{
				_vfDeltaNorm = 0.0;
				_vfImbNear = 0.0;
				_vfTotalVol = 0.0;
				_vfAbsDeltaFrac = 0.0;
			}
		}

		// ---------- Regime detection (large-tick proxy) ----------
		private void UpdateRegimeOnBar()
		{
			if (_spreadOneBuf == null || _spreadOneBuf.Length == 0) return;
			double sp = SpreadTicks();
			int isOne = (sp <= 1.01) ? 1 : 0;

			// circular buffer update
			int prev = _spreadOneBuf[_spreadOneIdx];
			_spreadOneBuf[_spreadOneIdx] = isOne;
			_spreadOneIdx = (_spreadOneIdx + 1) % _spreadOneBuf.Length;
			_spreadOneCount += (isOne - prev);

			double prob = (double)_spreadOneCount / (double)_spreadOneBuf.Length;
			_isLargeTick = prob >= LargeTickProbThreshold;
		}

		// ---------- Activity proxy (for concave sizing) ----------
		private void UpdateActivityOnBar()
		{
			if (_barVolBuf == null || _barVolBuf.Length == 0) return;
			UpdateVolumetricFeaturesIfNeeded();
			double v = _vfTotalVol;
			if (v <= 0) v = Volume[0];

			double prev = _barVolBuf[_barVolIdx];
			_barVolBuf[_barVolIdx] = v;
			_barVolIdx = (_barVolIdx + 1) % _barVolBuf.Length;
			_barVolSum += (v - prev);
		}

		private double AvgBarVol()
		{
			if (_barVolBuf == null || _barVolBuf.Length == 0) return Math.Max(1.0, Volume[0]);
			return Math.Max(1.0, _barVolSum / _barVolBuf.Length);
		}

		private int ComputeDynamicQuantity()
		{
			UpdateVolumetricFeaturesIfNeeded();
			double avgV = AvgBarVol();
			double curV = _vfTotalVol > 0 ? _vfTotalVol : Math.Max(1.0, Volume[0]);

			double volRatio = curV / Math.Max(1.0, avgV);
			double act = 0.5 * volRatio + 0.5 * _vfAbsDeltaFrac; // simple activity index
			double denom = Math.Sqrt(1.0 + Math.Max(0.0, SizeActivityK) * act);
			int q = (int)Math.Round(Math.Max(1.0, BaseQuantity / denom));
			return Math.Max(1, q);
		}

		// ---------- Baseline ΔRπ EWMA update ----------
		private void UpdateBaselineGaps(EventType type, double absMidChTicks)
		{
			double a = Math.Max(0.001, Math.Min(0.50, BaselineGapAlpha));
			absMidChTicks = Math.Max(0.0, absMidChTicks);
			// Clamp to avoid pathological spikes during halts/data gaps.
			absMidChTicks = Math.Min(50.0, absMidChTicks);

			switch (type)
			{
				case EventType.MO1:
					_dR_MO = (1.0 - a) * _dR_MO + a * Math.Max(0.25, absMidChTicks);
					break;
				case EventType.LO1:
					_dR_LO = (1.0 - a) * _dR_LO + a * Math.Max(0.25, absMidChTicks);
					break;
				case EventType.CA1:
					_dR_CA = (1.0 - a) * _dR_CA + a * Math.Max(0.25, absMidChTicks);
					break;
				default:
					break;
			}
		}

		private double GapForEvent(EventType type)
		{
			switch (type)
			{
				case EventType.MO1: return _dR_MO;
				case EventType.LO1: return _dR_LO;
				case EventType.CA1: return _dR_CA;
				default: return 0.0;
			}
		}

		private double ComputeBaselinePred()
		{
			if (_events.Count == 0) return 0.0;
			int L = Math.Max(10, BaselineLookbackEvents);
			double decay = Math.Max(0.80, Math.Min(0.999, BaselineDecayPerEvent));
			double w = 1.0;
			double sum = 0.0;
			int k = 0;

			var node = _events.First;
			// include from most recent backward
			while (node != null && k < L)
			{
				var ev = node.Value;
				if (ev.PriceChanged)
				{
					double g = GapForEvent(ev.Type);
					sum += w * g * ev.Eps;
					w *= decay;
					k++;
				}
				node = node.Next;
			}
			return sum;
		}

		// ---------- Gap-flex states update (small tick focused) ----------
		private void UpdateGapFlexStates(BookEvent ev)
		{
			double d = Math.Max(0.80, Math.Min(0.999, GapStateDecay));

			// Flow state: weight MO higher than quote events
			double wt = (ev.Type == EventType.MO0 || ev.Type == EventType.MO1) ? 1.0 : 0.7;
			_stateFlow = d * _stateFlow + (1.0 - d) * (wt * ev.Eps);

			// Gap state: EWMA of spread deviations
			double sp = ev.SpreadTicks;
			_avgSpreadEwma = 0.995 * _avgSpreadEwma + 0.005 * Math.Max(0.5, Math.Min(10.0, sp));
			double spDev = sp - _avgSpreadEwma;
			_stateGap = d * _stateGap + (1.0 - d) * spDev;

			// Imb state: use near imbalance
			_stateImb = d * _stateImb + (1.0 - d) * ev.ImbNear;
		}

		// ---------- Event emitter ----------
		private void EmitEvent(EventType type, int eps)
		{
			double mid = Mid();
			if (double.IsNaN(mid)) return;
			UpdateVolumetricFeaturesIfNeeded();
			_ofiProxy = 0.85 * _ofiProxy + 0.15 * _vfImbNear;

			double midChTicks = double.IsNaN(_prevMid) ? 0.0 : (mid - _prevMid) / TickSize;
			bool priceChanged = Math.Abs(midChTicks) >= 0.5; // half-tick threshold in ticks

			var ev = new BookEvent
			{
				Time = Time[0],
				Type = type,
				Eps = Math.Sign(eps),
				Mid = mid,
				MidChangeTicks = midChTicks,
				SpreadTicks = SpreadTicks(),
				DeltaNorm = _vfDeltaNorm,
				ImbNear = _vfImbNear,
				OfiProxy = _ofiProxy,
				PriceChanged = priceChanged
			};

			_events.AddFirst(ev);
			while (_events.Count > KernelLags + 5)
				_events.RemoveLast();

			// Induced-pattern bookkeeping
			if (priceChanged)
			{
				_lastPriceMoveTime = ev.Time;
				_lastPriceMoveDir = (midChTicks > 0) ? +1 : (midChTicks < 0 ? -1 : 0);
				_oppQuoteCount = 0;
			}
			else
			{
				// count opposing quote events shortly after a price move
				if (_lastPriceMoveDir != 0 && (ev.Time - _lastPriceMoveTime).TotalMilliseconds <= RefillWindowMs)
				{
					if (type == EventType.LO1 || type == EventType.CA1)
					{
						// if quote event pushes against last move direction, count it
						if (ev.Eps * _lastPriceMoveDir < 0)
							_oppQuoteCount++;
					}
				}
			}

			// Baseline gap EWMA update on price-changing proxies
			if (ev.PriceChanged)
				UpdateBaselineGaps(type, Math.Abs(midChTicks));

			// Gap-flex states update always
			UpdateGapFlexStates(ev);

			_eventCounter++;
			int n = Math.Max(1, TrainEveryNEvents);
			if (_events.Count >= KernelLags + 1 && (_eventCounter % n) == 0)
			{
				double y = ev.MidChangeTicks;
				BuildFeatureVector_Train(_x);
				RlsUpdate(_x, y);
			}

			_prevMid = mid;
			if (DebugPrint)
				Print($"[EV] {ev.Time:HH:mm:ss.fff} {type} eps={ev.Eps} midCh={ev.MidChangeTicks:F2} sp={ev.SpreadTicks:F1} dn={ev.DeltaNorm:F3} imb={ev.ImbNear:F3} ofi={ev.OfiProxy:F3} pc={ev.PriceChanged} dR(MO/LO/CA)={_dR_MO:F2}/{_dR_LO:F2}/{_dR_CA:F2} oppQ={_oppQuoteCount} large={_isLargeTick}");
		}

		private void BuildFeatureVector_Train(double[] x)
		{
			Array.Clear(x, 0, x.Length);
			var node = _events.First;
			if (node == null) return;
			node = node.Next; // skip current event
			int lag = 1;
			while (node != null && lag <= KernelLags)
			{
				int et = (int)node.Value.Type;
				int s = node.Value.Eps;
				int idx = (lag - 1) * 6 + et;
				x[idx] = s;
				lag++;
				node = node.Next;
			}

			UpdateVolumetricFeaturesIfNeeded();
			int o = _baseDim;
			x[o + 0] = SpreadTicks();
			x[o + 1] = _vfDeltaNorm;
			x[o + 2] = _vfImbNear;
			x[o + 3] = _ofiProxy;
			x[o + 4] = _isLargeTick ? 1.0 : 0.0;
			x[o + 5] = BaselineWeight * ComputeBaselinePred();
			x[o + 6] = _stateFlow;
			x[o + 7] = _stateGap;
			x[o + 8] = _stateImb;
		}

		private void BuildFeatureVector_Predict(double[] x)
		{
			Array.Clear(x, 0, x.Length);
			var node = _events.First;
			int lag = 1;
			while (node != null && lag <= KernelLags)
			{
				int et = (int)node.Value.Type;
				int s = node.Value.Eps;
				int idx = (lag - 1) * 6 + et;
				x[idx] = s;
				lag++;
				node = node.Next;
			}

			UpdateVolumetricFeaturesIfNeeded();
			int o = _baseDim;
			x[o + 0] = SpreadTicks();
			x[o + 1] = _vfDeltaNorm;
			x[o + 2] = _vfImbNear;
			x[o + 3] = _ofiProxy;
			x[o + 4] = _isLargeTick ? 1.0 : 0.0;
			x[o + 5] = BaselineWeight * ComputeBaselinePred();
			x[o + 6] = _stateFlow;
			x[o + 7] = _stateGap;
			x[o + 8] = _stateImb;
		}

		private void RlsUpdate(double[] x, double y)
		{
			double lambda = Math.Max(0.90, Math.Min(0.9999, RlsLambda));
			for (int i = 0; i < _featDim; i++)
			{
				double s = 0.0;
				for (int j = 0; j < _featDim; j++)
					s += _P[i, j] * x[j];
				_Px[i] = s;
			}

			double xTPx = 0.0;
			for (int i = 0; i < _featDim; i++)
				xTPx += x[i] * _Px[i];

			double denom = lambda + xTPx;
			if (Math.Abs(denom) < 1e-12) return;

			for (int i = 0; i < _featDim; i++)
				_k[i] = _Px[i] / denom;

			double yhat = 0.0;
			for (int i = 0; i < _featDim; i++)
				yhat += _w[i] * x[i];

			double err = y - yhat;
			for (int i = 0; i < _featDim; i++)
				_w[i] += _k[i] * err;

			for (int i = 0; i < _featDim; i++)
				for (int j = 0; j < _featDim; j++)
					_P[i, j] = (_P[i, j] - _k[i] * _Px[j]) / lambda;
		}

		private double PredictNextTicks()
		{
			if (_events.Count < KernelLags) return 0.0;
			BuildFeatureVector_Predict(_x);
			double yhat = 0.0;
			for (int i = 0; i < _featDim; i++)
				yhat += _w[i] * _x[i];
			return yhat;
		}

		// ---------- Market data (L1) ----------
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (e == null) return;

			// arrival-time proxy (PC clock) to estimate jitter/buffering
			DateTime arrive = Core.Globals.Now;
			bool isBA = (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask);
			RecordMarketDataArrival(arrive, isBA);

			if (e.MarketDataType == MarketDataType.Bid)
			{
				double prevBid = _bestBid;
				_bestBid = e.Price;
				if (UseQuoteEvents && !double.IsNaN(prevBid) && _bestBid > 0)
				{
					if (QuoteEventMinMs <= 0 || (Time[0] - _lastQuoteEventTime).TotalMilliseconds >= QuoteEventMinMs)
					{
						if (_bestBid > prevBid) EmitEvent(EventType.LO1, +1);
						else if (_bestBid < prevBid) EmitEvent(EventType.CA1, -1);
						_lastQuoteEventTime = Time[0];
					}
				}
				TryResolvePendingTrade();
				return;
			}

			if (e.MarketDataType == MarketDataType.Ask)
			{
				double prevAsk = _bestAsk;
				_bestAsk = e.Price;
				if (UseQuoteEvents && !double.IsNaN(prevAsk) && _bestAsk > 0)
				{
					if (QuoteEventMinMs <= 0 || (Time[0] - _lastQuoteEventTime).TotalMilliseconds >= QuoteEventMinMs)
					{
						if (_bestAsk < prevAsk) EmitEvent(EventType.LO1, +1);
						else if (_bestAsk > prevAsk) EmitEvent(EventType.CA1, -1);
						_lastQuoteEventTime = Time[0];
					}
				}
				TryResolvePendingTrade();
				return;
			}

			if (e.MarketDataType != MarketDataType.Last)
				return;

			if (MinTradeSize > 1 && e.Volume < MinTradeSize)
				return;

			if (double.IsNaN(_bestBid) || double.IsNaN(_bestAsk) || _bestBid <= 0 || _bestAsk <= 0)
				return;

			double p = e.Price;
			int side;
			if (p >= _bestAsk) side = +1;
			else if (p <= _bestBid) side = -1;
			else
			{
				double db = Math.Abs(p - _bestBid);
				double da = Math.Abs(p - _bestAsk);
				side = (da <= db) ? +1 : -1;
			}

			_pendingTrade = true;
			_pendingTradeTime = Time[0];
			_pendingSide = side;
			_pendingMid = Mid();
		}

		private void TryResolvePendingTrade()
		{
			if (!_pendingTrade) return;
			double dtms = (Time[0] - _pendingTradeTime).TotalMilliseconds;
			if (dtms < 0) return;

			if (dtms <= _pendingWindowMs)
			{
				double m = Mid();
				if (double.IsNaN(m) || double.IsNaN(_pendingMid)) return;
				bool moved = (_pendingSide == +1) ? (m > _pendingMid) : (m < _pendingMid);
				EmitEvent(moved ? EventType.MO1 : EventType.MO0, _pendingSide);
				_pendingTrade = false;
			}
			else if (dtms > _pendingWindowMs * 2)
			{
				EmitEvent(EventType.MO0, _pendingSide);
				_pendingTrade = false;
			}
		}

		// ---------- Entry gating: induced refill trap ----------
		private bool IsRefillTrap(int dir)
		{
			if (_lastPriceMoveDir == 0) return false;
			if ((Time[0] - _lastPriceMoveTime).TotalMilliseconds > RefillWindowMs) return false;

			if (dir == _lastPriceMoveDir && _oppQuoteCount >= RefillOpposingQuoteThreshold)
				return true;

			return false;
		}

		protected override void OnBarUpdate()
		{
			// Reset checks (run once per bar in OnEachTick mode)
			if (IsFirstTickOfBar)
			{
				// NEW: periodic reset has priority when enabled
				if (UsePeriodicReset)
					HandlePeriodicReset();
				else
					HandleDailyPreOpenReset();
			}

			if (CurrentBar < 5) return;
			TryResolvePendingTrade();

			UpdateRegimeOnBar();
			UpdateActivityOnBar();

			if (!InTradeWindow(Time[0])) return;

			if (_entryOrder != null && _entryOrder.OrderState == OrderState.Working && _entryLimitPx > 0)
			{
				double mid = Mid();
				if (!double.IsNaN(mid))
				{
					double awayTicks = Math.Abs(mid - _entryLimitPx) / TickSize;
					if (awayTicks > CancelEntryIfAwayTicks)
					{
						try { CancelOrder(_entryOrder); } catch { }
						_entryOrder = null;
						_entryLimitPx = 0;
					}
				}
			}

			if (Position.MarketPosition != MarketPosition.Flat) return;
			if (SpreadTicks() > MaxSpreadTicks) return;
			if (_events.Count < KernelLags) return;

			
double pred = PredictNextTicks();
_lastPred = pred;

int thrBase = _isLargeTick ? EntryThresholdTicksLarge : EntryThresholdTicksSmall;
int thr = EffectiveThreshold(thrBase);

// Latency/jitter gate: block trading when feed is stale or jittery
if (LatencyGateBlocks(out string lr))
{
	if (DebugPrint)
		Print($"[GATE] Latency/Jitter blocked | {lr} | p95={_jitterP95Ms:F1}ms med={_jitterMedianMs:F1}ms effLat={_effectiveLatencyMs:F1}ms");
	return;
}

int dir = 0;
if (pred >= thr) dir = +1;
else if (pred <= -thr) dir = -1;
else { _lastPredDir = 0; return; }
_lastPredDir = dir;

// Require signal stability under jitter (multi-tick confirmation)
if (!ConfirmSignal(dir))
{
	if (DebugPrint)
		Print($"[GATE] ConfirmSignal waiting | dir={dir} pred={pred:F2} thr={thr} cnt={_confirmCount}/{ConfirmEvents}");
	return;
}
			if (IsRefillTrap(dir))
			{
				if (DebugPrint)
					Print($"[GATE] RefillTrap blocked dir={dir} pred={pred:F2} oppQ={_oppQuoteCount}");
				return;
			}

			EnterWithBracket(dir);
		}

		private void EnterWithBracket(int dir)
		{
			SetStopLoss(CalculationMode.Ticks, StopTicks);
			SetProfitTarget(CalculationMode.Ticks, TargetTicks);

			_entrySubmitLocalTime = Core.Globals.Now;
			_entryWorkingLocalTime = Core.Globals.MinDate;

			double bid = _bestBid;
			double ask = _bestAsk;
			if (double.IsNaN(bid) || double.IsNaN(ask) || bid <= 0 || ask <= 0)
				return;

			int qty = ComputeDynamicQuantity();

			if (UseLimitEntries)
			{
				double px = (dir == +1) ? (bid - EffectiveLimitOffsetTicks(LimitOffsetTicks) * TickSize) : (ask + EffectiveLimitOffsetTicks(LimitOffsetTicks) * TickSize);
				px = Math.Round(px / TickSize) * TickSize;
				_entryLimitPx = px;

				if (dir == +1)
					EnterLongLimit(qty, px, "EISLER_NQ_ENH_LONG");
				else
					EnterShortLimit(qty, px, "EISLER_NQ_ENH_SHORT");
			}
			else
			{
				_entryLimitPx = 0;
				if (dir == +1) EnterLong(qty, "EISLER_NQ_ENH_LONG_MKT");
				else EnterShort(qty, "EISLER_NQ_ENH_SHORT_MKT");
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
			double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			if (order == null) return;

			if (order.Name != null && (order.Name.Contains("EISLER_NQ_ENH_LONG") || order.Name.Contains("EISLER_NQ_ENH_SHORT")))
			{
				if (orderState == OrderState.Working || orderState == OrderState.Accepted)
				{
					_entryOrder = order;
					if (_entryWorkingLocalTime == Core.Globals.MinDate) _entryWorkingLocalTime = Core.Globals.Now;
				}
				else if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
				{
					if (_entryOrder == order) _entryOrder = null;
					_entryLimitPx = 0;
					_entrySubmitLocalTime = Core.Globals.MinDate;
					_entryWorkingLocalTime = Core.Globals.MinDate;
				}
			}
		}
	}
}
