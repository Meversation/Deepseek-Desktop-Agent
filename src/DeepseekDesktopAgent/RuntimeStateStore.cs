using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

/// <summary>Agent 运行阶段，用于崩溃后判断上一轮停在哪。</summary>
public enum AgentPhase
{
    Idle,
    Thinking,
    ToolRunning,
    WaitingUser,
    Stopping
}

/// <summary>进程级运行态：与会话数据分开存放，避免改动 SessionStore 的 SchemaVersion。</summary>
public sealed class AgentRuntimeState
{
    public int Pid { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool CleanShutdown { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TurnId { get; set; }
    public AgentPhase Phase { get; set; } = AgentPhase.Idle;
    public List<string> PendingPrompts { get; set; } = new();
    public string LastToolKind { get; set; } = string.Empty;
    public string LastToolPayloadHash { get; set; } = string.Empty;
    public DateTimeOffset? LastToolStartedAt { get; set; }
}

/// <summary>扫描到的未正常结束记录。</summary>
public sealed record UncleanShutdownInfo(string FilePath, AgentRuntimeState? State, string Message);

/// <summary>
/// 运行态存储：runtime\&lt;pid&gt;.json 为每个进程的记录，runtime\last.json 为最近一次的副本。
/// 原子写（.tmp + 覆盖移动），读取损坏文件时跳过并记入 LastErrors。
/// </summary>
public sealed class RuntimeStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _gate = new();
    private readonly string _root;

    public RuntimeStateStore(string? root = null)
    {
        _root = root ?? DefaultRoot();
    }

    public IReadOnlyList<SessionLoadError> LastErrors { get; private set; } = Array.Empty<SessionLoadError>();

    public string RuntimeDir => Path.Combine(_root, "runtime");

    public static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepseekDesktopAgent");

    public string FileFor(int pid) => Path.Combine(RuntimeDir, pid.ToString() + ".json");
    public string LastFile => Path.Combine(RuntimeDir, "last.json");

    /// <summary>写入当前进程的运行态，同时刷新 last.json。</summary>
    public void Save(AgentRuntimeState state)
    {
        if (state == null) return;
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(RuntimeDir);
                var json = JsonSerializer.Serialize(state, JsonOpts);
                WriteAtomic(FileFor(state.Pid), json);
                WriteAtomic(LastFile, json);
            }
        }
        catch (Exception ex)
        {
            LastErrors = LastErrors.Append(new SessionLoadError(FileFor(state.Pid), ex.Message)).ToArray();
        }
    }

    /// <summary>读取 last.json；损坏或不存在时返回 null。</summary>
    public AgentRuntimeState? LoadLast()
    {
        return ReadFile(LastFile, new List<SessionLoadError>());
    }

    /// <summary>扫描所有非正常结束（CleanShutdown=false）且进程已不存在的记录。</summary>
    public IReadOnlyList<UncleanShutdownInfo> ScanUnclean()
    {
        var result = new List<UncleanShutdownInfo>();
        var errors = new List<SessionLoadError>();
        try
        {
            if (!Directory.Exists(RuntimeDir)) return result;
            foreach (var file in Directory.EnumerateFiles(RuntimeDir, "*.json"))
            {
                if (string.Equals(file, LastFile, StringComparison.OrdinalIgnoreCase)) continue;
                var state = ReadFile(file, errors);
                if (state == null) continue;
                if (state.CleanShutdown) continue;
                if (IsProcessAlive(state.Pid)) continue;
                result.Add(new UncleanShutdownInfo(file, state, BuildMessage(state)));
            }
            result.Sort((a, b) => (b.State?.StartedAt ?? DateTimeOffset.MinValue)
                .CompareTo(a.State?.StartedAt ?? DateTimeOffset.MinValue));
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(RuntimeDir, ex.Message));
        }
        LastErrors = errors;
        return result;
    }

    /// <summary>删除指定进程的记录文件；清理残留时使用。</summary>
    public bool Remove(int pid)
    {
        try
        {
            var file = FileFor(pid);
            if (!File.Exists(file)) return false;
            File.Delete(file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按最后工具类型给出崩溃影响说明。</summary>
    public static string BuildMessage(AgentRuntimeState state)
    {
        var phaseText = state.Phase switch
        {
            AgentPhase.Thinking => "网页端回复生成中",
            AgentPhase.ToolRunning => "工具执行中",
            AgentPhase.WaitingUser => "等待用户输入",
            AgentPhase.Stopping => "停止流程中",
            _ => "空闲"
        };

        var toolText = state.LastToolKind switch
        {
            "PowerShell" or "powershell" => "PowerShell 进程已随程序退出，脚本内的变量、函数与当前目录全部丢失，命令可能只执行到一半，目标文件与注册表/服务状态需要复核。",
            "Screenshot" or "screenshot" => "截图不产生本机副作用，可安全重试。",
            "UploadFile" or "uploadfile" => "文件仅被读取上传，可安全重试。",
            "HttpGetFile" or "httpgetfile" => "下载可能未完成，请检查目标文件大小或哈希后再决定是否重下。",
            "Mouse" or "mouse" or "Keyboard" or "keyboard" => "鼠标/键盘模拟中断时可能留下半完成的界面操作，请确认当前窗口状态。",
            _ => "没有记录到最后一次工具调用。"
        };

        var pending = state.PendingPrompts.Count > 0
            ? $"另有 {state.PendingPrompts.Count} 条排队消息未执行。"
            : "没有排队消息。";

        return $"上次运行在 {phaseText} 阶段异常结束。{toolText}{pending}";
    }

    private void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    private AgentRuntimeState? ReadFile(string path, List<SessionLoadError> errors)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<AgentRuntimeState>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(path, ex.Message));
            return null;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

internal static class RuntimeStateStoreSelfTest
{
    public static string[] Run(string root)
    {
        var failures = new List<string>();
        var testRoot = Path.Combine(root, "runtime_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RuntimeStateStore(testRoot);
            var state = new AgentRuntimeState
            {
                Pid = 999999,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CleanShutdown = false,
                SessionId = Guid.NewGuid(),
                TurnId = Guid.NewGuid(),
                Phase = AgentPhase.ToolRunning,
                PendingPrompts = { "排队一", "排队二" },
                LastToolKind = "PowerShell",
                LastToolPayloadHash = "abc123",
                LastToolStartedAt = DateTimeOffset.UtcNow.AddMinutes(-6)
            };
            store.Save(state);

            var last = store.LoadLast();
            if (last == null) failures.Add("last.json 读取失败");
            else
            {
                if (last.SessionId != state.SessionId) failures.Add("SessionId 未往返一致");
                if (last.PendingPrompts.Count != 2) failures.Add("排队消息数量不一致");
                if (last.Phase != AgentPhase.ToolRunning) failures.Add("Phase 不一致");
                if (last.LastToolPayloadHash != "abc123") failures.Add("工具哈希不一致");
            }

            state.CleanShutdown = true;
            state.Phase = AgentPhase.Idle;
            store.Save(state);
            var afterClean = store.LoadLast();
            if (afterClean == null || !afterClean.CleanShutdown) failures.Add("原子覆盖后未读到 CleanShutdown=true");

            var unclean = store.ScanUnclean();
            if (unclean.Count != 0) failures.Add("已标记正常退出的记录不应被判定为残留");

            var uncleanState = new AgentRuntimeState
            {
                Pid = 999998,
                StartedAt = DateTimeOffset.UtcNow,
                CleanShutdown = false,
                LastToolKind = "PowerShell"
            };
            store.Save(uncleanState);
            var scan = store.ScanUnclean();
            if (scan.Count != 1) failures.Add($"应扫描到 1 条残留，实际 {scan.Count}");
            else if (!scan[0].Message.Contains("PowerShell")) failures.Add("影响说明未包含工具类型");

            File.WriteAllText(Path.Combine(store.RuntimeDir, "broken.json"), "{ not json", new UTF8Encoding(false));
            var scanBroken = store.ScanUnclean();
            if (scanBroken.Count != 1) failures.Add("损坏文件不应影响有效记录的扫描");
            if (!store.LastErrors.Any(e => e.FilePath.EndsWith("broken.json", StringComparison.OrdinalIgnoreCase)))
                failures.Add("损坏文件未被记入 LastErrors");

            Parallel.For(0, 20, i =>
            {
                store.Save(new AgentRuntimeState
                {
                    Pid = 999000 + i,
                    StartedAt = DateTimeOffset.UtcNow,
                    CleanShutdown = true,
                    Phase = AgentPhase.Idle
                });
            });
            var parallelLast = store.LoadLast();
            if (parallelLast == null) failures.Add("并发写后 last.json 不可读");

            if (!store.Remove(999998)) failures.Add("删除残留记录失败");
            if (store.ScanUnclean().Any(s => s.State?.Pid == 999998)) failures.Add("删除后仍能扫描到该记录");
        }
        catch (Exception ex)
        {
            failures.Add("运行态存储自检异常: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
        }
        return failures.ToArray();
    }
}
