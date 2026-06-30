using System.Diagnostics;
using System.Text;

namespace K6LoadTestEngine.Services;

public class K6ProcessRunner
{
    /// <summary>
    /// Runs k6 with the given script and output JSON path.
    /// Returns (exitCode, allLogs) after the process completes.
    /// </summary>
    public async Task<(int ExitCode, string Logs)> RunAsync(
        string scriptPath,
        string resultJsonPath,
        CancellationToken ct = default)
    {
        var logs = new StringBuilder();
        logs.AppendLine($"[ENGINE] Starting k6 test at {DateTime.Now:HH:mm:ss}");
        logs.AppendLine($"[ENGINE] Script: {scriptPath}");
        logs.AppendLine($"[ENGINE] Output: {resultJsonPath}");
        logs.AppendLine();

        // Ensure any old result file is removed
        if (File.Exists(resultJsonPath))
            File.Delete(resultJsonPath);

        var psi = new ProcessStartInfo
        {
            FileName = "k6",
            Arguments = $"run --out json=\"{resultJsonPath}\" \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };

        // Collect stdout and stderr
        var outputLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLock) logs.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLock) logs.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return (-1, $"[ERROR] Failed to start k6 process: {ex.Message}\n" +
                        "Make sure k6 is installed and available in PATH.\n" +
                        "Install: winget install k6 OR https://k6.io/docs/get-started/installation/");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (-2, logs + "\n[ENGINE] Test cancelled by user.");
        }

        logs.AppendLine();
        logs.AppendLine($"[ENGINE] k6 exited with code {process.ExitCode} at {DateTime.Now:HH:mm:ss}");

        return (process.ExitCode, logs.ToString());
    }
}
