using System.Text.Json;
using K6LoadTestEngine.Models;

namespace K6LoadTestEngine.Services;

/// <summary>
/// Parses k6's NDJSON output (--out json) and builds a TestResult.
/// k6 JSON output format: each line is a metric data point.
/// Relevant metric names: http_req_duration, http_req_failed, vus, http_reqs
/// </summary>
public class K6ResultParser
{
    public TestResult Parse(string resultJsonPath, TestConfig config, string terminalLogs)
    {
        // Ensure config is normalised
        config.Normalise();

        int    pct   = config.PctValue;       // e.g. 95
        int    pctMs = config.PctThresholdMs; // e.g. 500
        string pctLabel = $"p{pct}";          // e.g. "p95"

        var result = new TestResult
        {
            TerminalLogs            = terminalLogs,
            PctValue                = pct,
            PctLabel                = pctLabel,
            PctThresholdMs          = pctMs,
            ErrorRateThresholdPercent = config.MaxErrorRatePercent,
        };

        if (!File.Exists(resultJsonPath))
        {
            result.Success      = false;
            result.ErrorMessage = "result.json not found. k6 may have failed to start or the test was too short.";
            return result;
        }

        try
        {
            ParseJsonFile(resultJsonPath, config, result);
            BuildThresholds(result, config);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success      = false;
            result.ErrorMessage = $"Failed to parse result.json: {ex.Message}";
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void ParseJsonFile(string path, TestConfig config, TestResult result)
    {
        var durations      = new List<double>();
        long totalRequests = 0;
        long failedRequests = 0;

        // For time-series (bucketed by second)
        var buckets = new SortedDictionary<int, BucketData>();

        using var reader = new StreamReader(path);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type",   out var typeProp))   continue;
                if (!root.TryGetProperty("metric", out var metricProp)) continue;
                if (!root.TryGetProperty("data",   out var dataProp))   continue;

                string type   = typeProp.GetString()   ?? "";
                string metric = metricProp.GetString() ?? "";

                if (type != "Point") continue;

                // Timestamp in seconds from epoch
                double timeEpoch = 0;
                if (dataProp.TryGetProperty("time", out var timeProp))
                {
                    if (timeProp.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(timeProp.GetString(), out var dt))
                    {
                        timeEpoch = (dt.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
                    }
                }

                double value = 0;
                if (dataProp.TryGetProperty("value", out var valueProp))
                {
                    value = valueProp.ValueKind == JsonValueKind.Number
                        ? valueProp.GetDouble()
                        : 0;
                }

                int bucket = (int)(timeEpoch % 86400);

                switch (metric)
                {
                    case "http_req_duration":
                        durations.Add(value);
                        totalRequests++;
                        GetBucket(buckets, bucket).AddDuration(value);
                        break;

                    case "http_req_failed":
                        if (value > 0.5) failedRequests++;
                        break;

                    case "vus":
                        GetBucket(buckets, bucket).VUs = (int)value;
                        break;

                    case "http_reqs":
                        GetBucket(buckets, bucket).Requests++;
                        break;
                }
            }
        }

        // ── Aggregate stats ───────────────────────────────────────────────────
        if (durations.Count == 0)
        {
            result.ErrorMessage = "No http_req_duration data points found in result.json.";
            return;
        }

        durations.Sort();
        result.TotalRequests   = totalRequests;
        result.SuccessRequests = totalRequests - failedRequests;
        result.MinDurationMs   = durations.First();
        result.MaxDurationMs   = durations.Last();
        result.AvgDurationMs   = durations.Average();
        result.MedDurationMs   = Percentile(durations, 0.50);
        result.P90DurationMs   = Percentile(durations, 0.90);

        // Dynamic percentile: use whatever PctValue the user chose
        double pctFraction  = config.PctValue / 100.0;
        result.PctActualMs  = Percentile(durations, pctFraction);

        result.ErrorRateActualPercent = totalRequests > 0
            ? (double)failedRequests / totalRequests * 100
            : 0;

        // ── Time series ───────────────────────────────────────────────────────
        if (buckets.Count > 0)
        {
            int minKey = buckets.Keys.First();
            foreach (var (key, b) in buckets)
            {
                var sorted = b.Durations.OrderBy(x => x).ToList();
                result.TimeSeries.Add(new TimeSeriesPoint
                {
                    TimeSeconds = key - minKey,
                    // P95Ms in TimeSeriesPoint is now the user's chosen percentile
                    P95Ms    = sorted.Count > 0 ? Percentile(sorted, pctFraction) : 0,
                    AvgMs    = sorted.Count > 0 ? sorted.Average() : 0,
                    ActiveVUs = b.VUs,
                    Rps      = b.Requests,
                });
            }

            double totalTime = result.TimeSeries.Last().TimeSeconds;
            result.AvgRps = totalTime > 0 ? totalRequests / totalTime : 0;
        }
    }

    private static void BuildThresholds(TestResult result, TestConfig config)
    {
        string label = $"p{config.PctValue}";

        result.PctPassed       = result.PctActualMs < config.PctThresholdMs;
        result.ErrorRatePassed = result.ErrorRateActualPercent < config.MaxErrorRatePercent;

        result.Thresholds = new List<ThresholdResult>
        {
            new ThresholdResult
            {
                Name   = $"Response Time ({label})",
                Target = $"< {config.PctThresholdMs}ms",
                Actual = $"{result.PctActualMs:F1}ms",
                Passed = result.PctPassed,
            },
            new ThresholdResult
            {
                Name   = "Error Rate",
                Target = $"< {config.MaxErrorRatePercent}%",
                Actual = $"{result.ErrorRateActualPercent:F2}%",
                Passed = result.ErrorRatePassed,
            },
        };
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double idx = p * (sorted.Count - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Count - 1);
        double frac = idx - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }

    private static BucketData GetBucket(SortedDictionary<int, BucketData> dict, int key)
    {
        if (!dict.TryGetValue(key, out var b))
        {
            b = new BucketData();
            dict[key] = b;
        }
        return b;
    }

    private class BucketData
    {
        public List<double> Durations { get; } = new();
        public int VUs      { get; set; }
        public int Requests { get; set; }

        public void AddDuration(double d) => Durations.Add(d);
    }
}
