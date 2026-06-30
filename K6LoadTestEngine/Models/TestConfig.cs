namespace K6LoadTestEngine.Models;

/// <summary>
/// Represents a single endpoint with its weight for traffic distribution.
/// </summary>
public class EndpointConfig
{
    /// <summary>Target URL for this endpoint</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP Method: GET, POST, PUT, DELETE, PATCH</summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Traffic weight (relative, not required to sum to 100).
    /// E.g. [70, 30] means 70% / 30% split.
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>Optional request body for POST/PUT/PATCH</summary>
    public string? RequestBody { get; set; }

    /// <summary>Per-endpoint request headers as key-value pairs</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Whether to use dynamic data (CSV/random) for this specific endpoint</summary>
    public bool UseDynamicData { get; set; } = false;

    /// <summary>Base64-encoded CSV content specific to this endpoint</summary>
    public string? CsvDataBase64 { get; set; }
}

public class TestConfig
{
    // ── Multi-endpoint list (replaces single Url + HttpMethod) ─────────────
    /// <summary>
    /// List of endpoints with weights. At least one is required.
    /// Backward compat: if Url is set but Endpoints is empty, it will be
    /// converted to a single-item list in Program.cs.
    /// </summary>
    public List<EndpointConfig> Endpoints { get; set; } = new();

    // ── Legacy single-endpoint fields (kept for backward compatibility) ────
    /// <summary>Legacy: used when Endpoints list is empty</summary>
    public string? Url { get; set; }

    /// <summary>Legacy: HTTP method for single-endpoint mode</summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>Total steady-state duration in seconds</summary>
    public int DurationSeconds { get; set; } = 30;

    /// <summary>Number of Pre-allocated Virtual Users during steady state</summary>
    public int VUs { get; set; } = 10;

    public int MaxVUsLimit { get; set; }

    /// <summary>Target requests per second (optional, overrides VU-based throughput display)</summary>
    public int TargetRps { get; set; } = 0;

    /// <summary>Ramp-up period in seconds</summary>
    public int RampUpSeconds { get; set; } = 5;

    /// <summary>Ramp-down period in seconds</summary>
    public int RampDownSeconds { get; set; } = 5;

    // ── Dynamic Percentile Threshold ───────────────────────────────────────
    /// <summary>
    /// Percentile value to use for the SLA threshold check.
    /// E.g. 95 = p95, 90 = p90, 99 = p99. Defaults to 95.
    /// </summary>
    public int PctValue { get; set; } = 95;

    /// <summary>
    /// Response time threshold in milliseconds for the selected percentile.
    /// E.g. 500 means p{PctValue} must be under 500ms.
    /// </summary>
    public int PctThresholdMs { get; set; } = 500;

    // ── Legacy threshold field (backward compat) ───────────────────────────
    /// <summary>Legacy: p95 SLA threshold in milliseconds</summary>
    public int P95ThresholdMs { get; set; } = 0;

    /// <summary>Maximum allowed error rate, 0-100 (e.g. 1 means 1%)</summary>
    public double MaxErrorRatePercent { get; set; } = 1.0;

    /// <summary>Optional request headers as key=value pairs (applied to all endpoints)</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>Use random dynamic data in requests</summary>
    public bool UseDynamicData { get; set; } = false;

    /// <summary>Base64-encoded CSV content (optional)</summary>
    public string? CsvDataBase64 { get; set; }

    /// <summary>
    /// Normalises the config so the rest of the pipeline always works with
    /// the Endpoints list and PctThresholdMs / PctValue fields.
    /// </summary>
    public void Normalise()
    {
        // --- Legacy single-URL migration ---
        if (Endpoints.Count == 0 && !string.IsNullOrWhiteSpace(Url))
        {
            Endpoints.Add(new EndpointConfig
            {
                Url        = Url,
                HttpMethod = HttpMethod,
                Weight     = 100,
                RequestBody = null,
            });
        }

        // --- Legacy P95ThresholdMs migration ---
        if (P95ThresholdMs > 0 && PctThresholdMs == 500)
        {
            // Caller sent the old field — use it
            PctThresholdMs = P95ThresholdMs;
        }

        // Clamp percentile
        if (PctValue < 1)  PctValue = 1;
        if (PctValue > 99) PctValue = 99;

        // Ensure all weights >= 1
        foreach (var ep in Endpoints)
            if (ep.Weight < 1) ep.Weight = 1;
    }
}
