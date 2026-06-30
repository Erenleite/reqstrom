using K6LoadTestEngine.Models;
using System.Text;

namespace K6LoadTestEngine.Services;

public class K6ScriptGenerator
{
    private readonly string _tempDir;

    public K6ScriptGenerator()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "k6-load-engine");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Generates a k6 test script based on the provided config.
    ///
    /// Executor strategy:
    ///   TargetRps > 0  →  ramping-arrival-rate  (fixed req/s)
    ///   TargetRps = 0  →  ramping-vus            (VU-based)
    ///
    /// Multi-endpoint support:
    ///   When Endpoints has more than one item, a weighted random picker is
    ///   injected into the k6 script so each virtual iteration selects an
    ///   endpoint proportional to its weight.
    ///
    /// Returns the path to the generated .js file.
    /// </summary>
    public string GenerateScript(TestConfig config)
    {
        // Ensure config is normalised (single URL → Endpoints list, etc.)
        config.Normalise();

        var sb = new StringBuilder();

        // ── Imports ───────────────────────────────────────────────────────────
        sb.AppendLine("import http from 'k6/http';");
        sb.AppendLine("import { check } from 'k6';");
        if (config.TargetRps <= 0)
            sb.AppendLine("import { sleep } from 'k6';");
        
        bool anyCsv = config.Endpoints.Any(e => !string.IsNullOrWhiteSpace(e.CsvDataBase64));
        if (anyCsv)
            sb.AppendLine("import papaparse from 'https://jslib.k6.io/papaparse/5.1.1/index.js';");

        sb.AppendLine();

        // ── Options ───────────────────────────────────────────────────────────
        double errorRate    = config.MaxErrorRatePercent / 100.0;
        string errorRateStr = errorRate.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        int    pct          = config.PctValue;          // e.g. 95
        int    pctMs        = config.PctThresholdMs;    // e.g. 500

        if (config.TargetRps > 0)
        {
            int safeMaxVUs = config.MaxVUsLimit > 0 ? config.MaxVUsLimit : config.VUs * 5;
            if (safeMaxVUs < config.VUs) safeMaxVUs = config.VUs;

            sb.AppendLine("export const options = {");
            sb.AppendLine("  scenarios: {");
            sb.AppendLine("    load: {");
            sb.AppendLine("      executor: 'ramping-arrival-rate',");
            sb.AppendLine("      startRate: 0,");
            sb.AppendLine("      timeUnit: '1s',");
            sb.AppendLine($"      preAllocatedVUs: {config.VUs},");
            sb.AppendLine($"      maxVUs: {safeMaxVUs},");
            sb.AppendLine("      stages: [");
            if (config.RampUpSeconds > 0)
                sb.AppendLine($"        {{ duration: '{config.RampUpSeconds}s', target: {config.TargetRps} }},");
            sb.AppendLine($"        {{ duration: '{config.DurationSeconds}s', target: {config.TargetRps} }},");
            if (config.RampDownSeconds > 0)
                sb.AppendLine($"        {{ duration: '{config.RampDownSeconds}s', target: 0 }},");
            sb.AppendLine("      ],");
            sb.AppendLine("    },");
            sb.AppendLine("  },");
            sb.AppendLine("  thresholds: {");
            sb.AppendLine($"    'http_req_duration': ['p({pct})<{pctMs}'],");
            sb.AppendLine($"    'http_req_failed': ['rate<{errorRateStr}'],");
            sb.AppendLine("  },");
            sb.AppendLine("};");
        }
        else
        {
            sb.AppendLine("export const options = {");
            sb.AppendLine("  stages: [");
            if (config.RampUpSeconds > 0)
                sb.AppendLine($"    {{ duration: '{config.RampUpSeconds}s', target: {config.VUs} }},");
            sb.AppendLine($"    {{ duration: '{config.DurationSeconds}s', target: {config.VUs} }},");
            if (config.RampDownSeconds > 0)
                sb.AppendLine($"    {{ duration: '{config.RampDownSeconds}s', target: 0 }},");
            sb.AppendLine("  ],");
            sb.AppendLine("  thresholds: {");
            sb.AppendLine($"    'http_req_duration': ['p({pct})<{pctMs}'],");
            sb.AppendLine($"    'http_req_failed': ['rate<{errorRateStr}'],");
            sb.AppendLine("  },");
            sb.AppendLine("};");
        }

        sb.AppendLine();

        // ── CSV Data ─────────────────────────────────────────────────────────
        var csvVarNames = new string[config.Endpoints.Count];
        for (int i = 0; i < config.Endpoints.Count; i++)
        {
            var ep = config.Endpoints[i];
            if (!string.IsNullOrWhiteSpace(ep.CsvDataBase64))
            {
                try
                {
                    byte[] csvBytes = Convert.FromBase64String(ep.CsvDataBase64);
                    string epCsvPath = Path.Combine(_tempDir, $"data_ep{i}_{DateTime.Now:HHmmss}.csv");
                    File.WriteAllBytes(epCsvPath, csvBytes);
                    sb.AppendLine($"const csvData_{i} = papaparse.parse(open('{EscapeJsString(epCsvPath)}'), {{ header: true, dynamicTyping: false }}).data;");
                    csvVarNames[i] = $"csvData_{i}";
                }
                catch
                {
                    csvVarNames[i] = "null";
                }
            }
            else
            {
                csvVarNames[i] = "null";
            }
        }
        sb.AppendLine();

        // ── Weighted endpoint table ───────────────────────────────────────────
        // Emit the endpoint array and a pickEndpoint() helper so every iteration
        // randomly selects a target proportional to its weight.
        sb.AppendLine("// ── Weighted Endpoint Routing ────────────────────────────────────────");
        sb.AppendLine("const ENDPOINTS = [");
        for (int i = 0; i < config.Endpoints.Count; i++)
        {
            var ep = config.Endpoints[i];
            string urlEsc    = EscapeJsString(ep.Url);
            string methodEsc = EscapeJsString(ep.HttpMethod.ToUpper());
            string bodyEsc   = ep.RequestBody != null ? EscapeJsString(ep.RequestBody) : "";
            string useDyn    = ep.UseDynamicData.ToString().ToLower();
            string csvVar    = csvVarNames[i];
            
            // Serialize headers dictionary
            var headersList = new List<string>();
            foreach (var kvp in ep.Headers)
            {
                if (kvp.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (kvp.Key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                headersList.Add($"'{EscapeJsString(kvp.Key)}': '{EscapeJsString(kvp.Value)}'");
            }
            string headersJson = "{" + string.Join(", ", headersList) + "}";

            sb.AppendLine($"  {{ url: '{urlEsc}', method: '{methodEsc}', weight: {ep.Weight}, body: '{bodyEsc}', headers: {headersJson}, useDynamicData: {useDyn}, csvData: {csvVar} }},");
        }
        sb.AppendLine("];");
        sb.AppendLine();
        sb.AppendLine("const TOTAL_WEIGHT = ENDPOINTS.reduce((s, e) => s + e.weight, 0);");
        sb.AppendLine();
        sb.AppendLine("function pickEndpoint() {");
        sb.AppendLine("  let r = Math.random() * TOTAL_WEIGHT;");
        sb.AppendLine("  for (const ep of ENDPOINTS) {");
        sb.AppendLine("    r -= ep.weight;");
        sb.AppendLine("    if (r <= 0) return ep;");
        sb.AppendLine("  }");
        sb.AppendLine("  return ENDPOINTS[ENDPOINTS.length - 1];");
        sb.AppendLine("}");
        sb.AppendLine();

        // ── Default function ──────────────────────────────────────────────────
        sb.AppendLine("export default function () {");

        // Pick endpoint for this iteration (moved up so we can use its headers)
        sb.AppendLine("  // Pick endpoint based on weight");
        sb.AppendLine("  const ep = pickEndpoint();");
        sb.AppendLine();

        // Base parameters with endpoint-specific headers
        sb.AppendLine("  const params = {");
        sb.AppendLine("    headers: Object.assign({");
        sb.AppendLine("      'Content-Type': 'application/json',");
        foreach (var header in config.Headers)
        {
            var parts = header.Split('=', 2);
            if (parts.Length == 2)
                sb.AppendLine($"      '{EscapeJsString(parts[0])}': '{EscapeJsString(parts[1])}',");
        }
        sb.AppendLine("    }, ep.headers)");
        sb.AppendLine("  };");
        sb.AppendLine();

        // Initialize defaults
        sb.AppendLine("  let urlExprBase = ep.url;");
        sb.AppendLine("  let reqBody = null;");
        sb.AppendLine("  if (ep.method === 'POST' || ep.method === 'PUT' || ep.method === 'PATCH') {");
        sb.AppendLine("    reqBody = ep.body !== '' ? ep.body : '{}';");
        sb.AppendLine("  }");
        sb.AppendLine();

        // Per-endpoint dynamic data
        // Per-endpoint dynamic data
        sb.AppendLine("  if (ep.useDynamicData) {");
        sb.AppendLine("    if (ep.csvData && ep.csvData.length > 0) {");
        sb.AppendLine("      const rawRow = ep.csvData[Math.floor(Math.random() * ep.csvData.length)];");
        sb.AppendLine("      let hasPlaceholder = false;");
        sb.AppendLine("      for (const key in rawRow) {");
        sb.AppendLine("        const cleanKey = key.replace(/^\\uFEFF/, '');");
        sb.AppendLine("        const placeholder = '{{' + cleanKey + '}}';");
        sb.AppendLine("        if (urlExprBase.includes(placeholder) || (reqBody && reqBody.includes(placeholder))) {");
        sb.AppendLine("          hasPlaceholder = true;");
        sb.AppendLine("          break;");
        sb.AppendLine("        }");
        sb.AppendLine("      }");
        sb.AppendLine("      if (hasPlaceholder) {");
        sb.AppendLine("        for (const key in rawRow) {");
        sb.AppendLine("          if (rawRow[key] === null) continue;");
        sb.AppendLine("          const cleanKey = key.replace(/^\\uFEFF/, '');");
        sb.AppendLine("          let val = String(rawRow[key]).trim();");
        sb.AppendLine("          if (reqBody) {");
        sb.AppendLine("             let jsonSafeVal = JSON.stringify(val);");
        sb.AppendLine("             jsonSafeVal = jsonSafeVal.substring(1, jsonSafeVal.length - 1);");
        sb.AppendLine("             const placeholder = '{{' + cleanKey + '}}';");
        sb.AppendLine("             reqBody = reqBody.split(placeholder).join(jsonSafeVal);");
        sb.AppendLine("          }");
        sb.AppendLine("          const placeholder = '{{' + cleanKey + '}}';");
        sb.AppendLine("          urlExprBase = urlExprBase.split(placeholder).join(val);");
        sb.AppendLine("        }");
        sb.AppendLine("      } else {");
        sb.AppendLine("        // No {{placeholder}} found — smart merge: patch existing body with CSV columns");
        sb.AppendLine("        if (ep.method === 'POST' || ep.method === 'PUT' || ep.method === 'PATCH') {");
        sb.AppendLine("          try {");
        sb.AppendLine("            const baseBody = reqBody ? JSON.parse(reqBody) : {};");
        sb.AppendLine("            for (const key in rawRow) {");
        sb.AppendLine("              const cleanKey = key.replace(/^\\uFEFF/, '').trim();");
        sb.AppendLine("              if (!cleanKey || rawRow[key] === null || rawRow[key] === undefined) continue;");
        sb.AppendLine("              const strVal = String(rawRow[key]).trim();");
        sb.AppendLine("              if (strVal === '') continue;");
        sb.AppendLine("              const originalVal = baseBody[cleanKey];");
        sb.AppendLine("              if (typeof originalVal === 'number') {");
        sb.AppendLine("                const num = Number(strVal);");
        sb.AppendLine("                baseBody[cleanKey] = isNaN(num) ? strVal : num;");
        sb.AppendLine("              } else if (typeof originalVal === 'boolean') {");
        sb.AppendLine("                baseBody[cleanKey] = strVal === 'true';");
        sb.AppendLine("              } else {");
        sb.AppendLine("                if (strVal.startsWith('[') && strVal.endsWith(']')) {");
        sb.AppendLine("                  try { baseBody[cleanKey] = JSON.parse(strVal); } catch(e) { baseBody[cleanKey] = strVal; }");
        sb.AppendLine("                } else if (strVal.startsWith('{') && strVal.endsWith('}')) {");
        sb.AppendLine("                  try { baseBody[cleanKey] = JSON.parse(strVal); } catch(e) { baseBody[cleanKey] = strVal; }");
        sb.AppendLine("                } else {");
        sb.AppendLine("                  baseBody[cleanKey] = strVal;");
        sb.AppendLine("                }");
        sb.AppendLine("              }");
        sb.AppendLine("            }");
        sb.AppendLine("            reqBody = JSON.stringify(baseBody);");
        sb.AppendLine("          } catch(e) {");
        sb.AppendLine("            // JSON parse of existing body failed — leave reqBody unchanged");
        sb.AppendLine("          }");
        sb.AppendLine("        }");
        sb.AppendLine("      }");
        sb.AppendLine("    } else {");
        sb.AppendLine("      const randomId = Math.floor(Math.random() * 10000);");
        sb.AppendLine("      if (urlExprBase.includes('{{id}}') || (reqBody && reqBody.includes('{{id}}'))) {");
        sb.AppendLine("        urlExprBase = urlExprBase.split('{{id}}').join(String(randomId));");
        sb.AppendLine("        if (reqBody) reqBody = reqBody.split('{{id}}').join(String(randomId));");
        sb.AppendLine("      } else {");
        sb.AppendLine("        urlExprBase = `${ep.url}${randomId}`;");
        sb.AppendLine("        if (ep.method === 'POST' || ep.method === 'PUT' || ep.method === 'PATCH') {");
        sb.AppendLine("          reqBody = ep.body !== '' ? ep.body : JSON.stringify({ id: randomId });");
        sb.AppendLine("        }");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine();

        // HTTP dispatch via http.request for method flexibility
        sb.AppendLine("  // Dispatch request");
        sb.AppendLine("  const res = http.request(ep.method, urlExprBase, reqBody, params);");
        sb.AppendLine();

        // Assertions
        sb.AppendLine("  check(res, {");
        sb.AppendLine("    'status is 2xx': (r) => r.status >= 200 && r.status < 300,");
        sb.AppendLine($"    'response time OK': (r) => r.timings.duration < {pctMs * 2},");
        sb.AppendLine("  });");

        // sleep only for VU-based mode
        if (config.TargetRps <= 0)
        {
            sb.AppendLine();
            sb.AppendLine("  sleep(1);");
        }

        sb.AppendLine("}");

        // ── Write to temp file ────────────────────────────────────────────────
        string scriptPath = Path.Combine(_tempDir, $"test_{DateTime.Now:yyyyMMddHHmmss}.js");
        File.WriteAllText(scriptPath, sb.ToString());
        return scriptPath;
    }

    public string GetResultJsonPath()
        => Path.Combine(_tempDir, "result.json");

    public string GetCsvPath(string originalName)
        => Path.Combine(_tempDir, originalName);

    private static string EscapeJsString(string s)
        => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`").Replace("\r", "").Replace("\n", "\\n");
}
