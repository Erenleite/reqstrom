namespace K6LoadTestEngine.Models;

public class TimeSeriesPoint
{
    public double TimeSeconds { get; set; }
    public double P95Ms { get; set; }
    public double AvgMs { get; set; }
    public int ActiveVUs { get; set; }
    public double Rps { get; set; }
}

public class ThresholdResult
{
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

public class TestResult
{
    // ── Status ──────────────────────────────────────────────────
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    // ── Dynamic Percentile Threshold ────────────────────────────
    /// <summary>e.g. "p95", "p90", "p99"</summary>
    public string PctLabel { get; set; } = "p95";

    /// <summary>The percentile integer used (e.g. 95, 90, 99)</summary>
    public int PctValue { get; set; } = 95;

    public double PctActualMs { get; set; }
    public double PctThresholdMs { get; set; }
    public bool PctPassed { get; set; }

    // ── Backward-compat aliases (kept so old frontend code still works) ──
    public double P95ActualMs   => PctActualMs;
    public double P95ThresholdMs => PctThresholdMs;
    public bool   P95Passed      => PctPassed;

    // ── Error rate ───────────────────────────────────────────────
    public double ErrorRateActualPercent { get; set; }
    public double ErrorRateThresholdPercent { get; set; }
    public bool ErrorRatePassed { get; set; }

    public List<ThresholdResult> Thresholds { get; set; } = new();

    // ── Summary stats ───────────────────────────────────────────
    public long TotalRequests { get; set; }
    public long SuccessRequests { get; set; }
    public double MinDurationMs { get; set; }
    public double MaxDurationMs { get; set; }
    public double AvgDurationMs { get; set; }
    public double MedDurationMs { get; set; }
    public double P90DurationMs { get; set; }
    public double AvgRps { get; set; }

    // ── Time-series data for chart ───────────────────────────────
    public List<TimeSeriesPoint> TimeSeries { get; set; } = new();

    // ── Raw k6 terminal output ───────────────────────────────────
    public string TerminalLogs { get; set; } = string.Empty;
}
