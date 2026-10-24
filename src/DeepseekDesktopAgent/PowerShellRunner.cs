using System.Diagnostics;
using System.Text;

namespace DeepseekDesktopAgent;

public sealed class PsRunResult
{
    public string Output { get; init; } = "";
    public string OutFile { get; init; } = "";
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }
}

public sealed class PowerShellRunner : IDisposable
{
    private readonly string _tempDir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _writeLock = new();
    private Process? _current;

    public event Action<string>? OutputLine;

    public bool IsRunning => _current is { HasExited: false };

    public PowerShellRunner(string tempDir)
    {
        _tempDir = tempDir;
    }

    public async Task<PsRunResult> RunAsync(string code, string workDir, CancellationToken ct, int timeoutMs = 300000)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var scriptPath = Path.Combine(_tempDir, $"ps_{stamp}.ps1");
            var outPath = Path.Combine(_tempDir, $"ps_{stamp}_out.txt");

            var script = BuildScript(code);
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(true), ct);

            var output = new StringBuilder();
            var totalChars = 0;
            var truncated = false;

            using var outWriter = new StreamWriter(outPath, false, new UTF8Encoding(true)) { AutoFlush = true };
            void WriteLine(string? line)
            {
                if (line == null) return;
                lock (_writeLock)
                {
                    try { outWriter.WriteLine(line); } catch { }
                    if (!truncated)
                    {
                        if (totalChars + line.Length > 4_000_000)
                        {
                            truncated = true;
                            output.AppendLine("[...输出过长，已截断，请查看输出文件...]");
                        }
                        else
                        {
                            output.AppendLine(line);
                            totalChars += line.Length + 2;
                        }
                    }
                }
                try { OutputLine?.Invoke(line); } catch { }
            }

            var psi = new ProcessStartInfo(FindPowerShell())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Directory.Exists(workDir) ? workDir : _tempDir
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _current = process;
            process.Start();

            var stdoutTask = PumpAsync(process.StandardOutput, WriteLine, ct);
            var stderrTask = PumpAsync(process.StandardError, WriteLine, ct);

            var timedOut = false;
            var canceled = false;
            using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                waitCts.CancelAfter(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = ct.IsCancellationRequested;
                    timedOut = !canceled;
                    TryKill(process);
                }
            }

            if (process.HasExited && process.ExitCode != 0)
            {
                WriteLine($"");
                WriteLine($"[进程退出码] {process.ExitCode}");
            }
            if (timedOut)
            {
                WriteLine("");
                WriteLine($"[执行超时] 已终止命令（{timeoutMs / 1000} 秒）");
            }
            else if (canceled)
            {
                WriteLine("");
                WriteLine("[已取消] 用户停止了当前命令");
            }

            try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }

            return new PsRunResult
            {
                Output = output.ToString().TrimEnd(),
                OutFile = outPath,
                ExitCode = process.HasExited ? process.ExitCode : -1,
                TimedOut = timedOut,
                Canceled = canceled
            };
        }
        finally
        {
            _current = null;
            _gate.Release();
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string?> sink, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                sink(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static string BuildScript(string code)
    {
        var header = new StringBuilder();
        header.AppendLine("# Deepseek-Desktop-Agent PowerShell runner");
        header.AppendLine("$ErrorActionPreference = 'Continue'");
        header.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        header.AppendLine("try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch { }");
        header.AppendLine("try { $OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch { }");
        header.AppendLine(code);
        return header.ToString();
    }

    private static string FindPowerShell()
    {
        var winPs = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPs)) return winPs;

        var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh)) return pwsh;
        return "pwsh.exe";
    }

    public void KillCurrent()
    {
        var proc = _current;
        if (proc != null) TryKill(proc);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public void Dispose()
    {
        KillCurrent();
        _gate.Dispose();
    }
}
