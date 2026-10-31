using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepseekDesktopAgent;

// ==================== 数据模型 ====================

/// <summary>轮次状态：running / completed / stopped / failed。</summary>
public enum TurnStatus { Running, Completed, Stopped, Failed }

/// <summary>停止方式：none / immediate / safe。</summary>
public enum StopMode { None, Immediate, Safe }

/// <summary>一次"用户输入 + 一次完整回复"的轮次记录。</summary>
public sealed class TurnRecord
{
    public Guid TurnId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? Reply { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public List<string> Files { get; set; } = new();
    public TurnStatus Status { get; set; } = TurnStatus.Running;
    public StopMode StopMode { get; set; } = StopMode.None;
}

/// <summary>会话记录，一个会话对应磁盘上一个 JSON 文件。</summary>
public sealed class SessionRecord
{
    public int SchemaVersion { get; set; } = 1;
    public Guid SessionId { get; set; }
    public string WorkDir { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>该会话对应的 DeepSeek 网页地址；旧会话可能为空。</summary>
    public string? WebUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TurnRecord> Turns { get; set; } = new();
}

/// <summary>会话摘要，用于列表展示。</summary>
public sealed class SessionSummary
{
    public Guid SessionId { get; set; }
    public string WorkDir { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int TurnCount { get; set; }
}

/// <summary>加载会话时遇到的一条错误记录。</summary>
public sealed record SessionLoadError(string FilePath, string Message);

// ==================== 持久化实现 ====================

/// <summary>
/// 会话数据持久化模块，不引用 WinForms，仅依赖 BCL。
/// 每个 Session 一个 JSON 文件（文件名 = sessionId）；写入走 .tmp + File.Move 原子替换；
/// 同一 sessionId 的进程内读写在 lock 内串行化；读取对损坏文件做容错跳过。
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _root;
    private readonly ConcurrentDictionary<Guid, object> _locks = new();
    private readonly object _errorLock = new();
    private IReadOnlyList<SessionLoadError> _lastErrors = Array.Empty<SessionLoadError>();

    /// <summary>最近一次 LoadSession / ListSessions 收集到的错误（损坏文件等）。</summary>
    public IReadOnlyList<SessionLoadError> LastErrors
    {
        get { lock (_errorLock) return _lastErrors; }
    }

    /// <summary>默认根目录：%LOCALAPPDATA%\DeepseekDesktopAgent\sessions。</summary>
    public static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepseekDesktopAgent", "sessions");

    /// <param name="root">自定义根目录；传 null/空白 走 <see cref="DefaultRoot"/>。</param>
    public SessionStore(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root) ? DefaultRoot() : root!;
        Directory.CreateDirectory(_root);
    }

    // -------------------- 公开 API --------------------

    /// <summary>创建会话并立即落盘。</summary>
    public SessionRecord CreateSession(string workDir, string? title = null)
    {
        var now = DateTimeOffset.UtcNow;
        var rec = new SessionRecord
        {
            SchemaVersion = 1,
            SessionId = Guid.NewGuid(),
            WorkDir = workDir,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now,
            Turns = new List<TurnRecord>()
        };
        WriteSession(rec);
        return rec;
    }

    /// <summary>在指定会话末尾追加一个 running 轮次并落盘。</summary>
    public TurnRecord BeginTurn(Guid sessionId, string prompt)
    {
        lock (GetLock(sessionId))
        {
            var rec = LoadOrThrow(sessionId);
            var turn = new TurnRecord
            {
                TurnId = Guid.NewGuid(),
                Prompt = prompt ?? string.Empty,
                Reply = null,
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = null,
                Files = new List<string>(),
                Status = TurnStatus.Running,
                StopMode = StopMode.None
            };
            rec.Turns.Add(turn);
            rec.UpdatedAt = DateTimeOffset.UtcNow;
            WriteSession(rec);
            return turn;
        }
    }

    /// <summary>按 turnId 完成轮次：写入 reply / files / status / stopMode。</summary>
    public void CompleteTurn(Guid sessionId, Guid turnId, string reply, IEnumerable<string> files, string status, string stopMode)
    {
        lock (GetLock(sessionId))
        {
            var rec = LoadOrThrow(sessionId);
            var turn = rec.Turns.FirstOrDefault(t => t.TurnId == turnId)
                ?? throw new InvalidOperationException($"会话 {sessionId} 中不存在轮次 {turnId}");
            turn.Reply = reply;
            turn.EndedAt = DateTimeOffset.UtcNow;
            turn.Files = files?.ToList() ?? new List<string>();
            turn.Status = ParseStatus(status);
            turn.StopMode = ParseStopMode(stopMode);
            rec.UpdatedAt = DateTimeOffset.UtcNow;
            WriteSession(rec);
        }
    }

    /// <summary>列出会话摘要；workDir 非空时按工作目录过滤；按 UpdatedAt 倒序返回。</summary>
    public IReadOnlyList<SessionSummary> ListSessions(string? workDir = null)
    {
        var list = new List<SessionSummary>();
        var errors = new List<SessionLoadError>();
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
            {
                var rec = TryLoad(file, errors);
                if (rec is null) continue;
                if (!string.IsNullOrEmpty(workDir) &&
                    !string.Equals(rec.WorkDir, workDir, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(new SessionSummary
                {
                    SessionId = rec.SessionId,
                    WorkDir = rec.WorkDir,
                    Title = rec.Title,
                    CreatedAt = rec.CreatedAt,
                    UpdatedAt = rec.UpdatedAt,
                    TurnCount = rec.Turns.Count
                });
            }
        }
        list.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        SetErrors(errors);
        return list;
    }

    /// <summary>按 sessionId 读取整个会话；不存在或损坏返回 null。</summary>
    public SessionRecord? LoadSession(Guid sessionId)
    {
        lock (GetLock(sessionId))
        {
            var path = PathFor(sessionId);
            var errors = new List<SessionLoadError>();
            if (!File.Exists(path)) { SetErrors(errors); return null; }
            var rec = TryLoad(path, errors);
            SetErrors(errors);
            return rec;
        }
    }

    /// <summary>删除会话文件；不存在返回 false。</summary>
    public bool DeleteSession(Guid sessionId)
    {
        lock (GetLock(sessionId))
        {
            var path = PathFor(sessionId);
            if (!File.Exists(path)) return false;
            try { File.Delete(path); return true; }
            catch { return false; }
        }
    }

    /// <summary>更新会话标题并刷新 UpdatedAt；会话不存在或损坏返回 false，不抛异常。</summary>
    public bool UpdateTitle(Guid sessionId, string? title)
    {
        lock (GetLock(sessionId))
        {
            try
            {
                var rec = LoadOrThrow(sessionId);
                rec.Title = title;
                rec.UpdatedAt = DateTimeOffset.UtcNow;
                WriteSession(rec);
                return true;
            }
            catch { return false; }
        }
    }
    /// <summary>记录会话对应的网页地址；会话不存在或地址非法时返回 false。</summary>
    public bool UpdateWebUrl(Guid sessionId, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (GetLock(sessionId))
        {
            try
            {
                var rec = LoadOrThrow(sessionId);
                if (rec.WebUrl == url) return true;
                rec.WebUrl = url;
                WriteSession(rec);
                return true;
            }
            catch { return false; }
        }
    }
    // -------------------- 内部实现 --------------------

    private string PathFor(Guid id) => Path.Combine(_root, id.ToString("D") + ".json");

    private object GetLock(Guid id) => _locks.GetOrAdd(id, _ => new object());

    private void SetErrors(List<SessionLoadError> errors)
    {
        lock (_errorLock) _lastErrors = errors.ToArray();
    }

    /// <summary>原子写入：先写同目录 .tmp，再用 File.Move(overwrite: true) 替换。</summary>
    private void WriteSession(SessionRecord rec)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(rec.SessionId);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(rec, JsonOpts);
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private SessionRecord LoadOrThrow(Guid sessionId)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            throw new InvalidOperationException($"会话文件不存在：{path}");
        var errors = new List<SessionLoadError>();
        var rec = TryLoad(path, errors);
        if (rec is null)
            throw new InvalidOperationException($"会话 {sessionId} 已损坏：{errors[0].Message}");
        return rec;
    }

    /// <summary>读取并反序列化；任何异常/校验失败都只记错误并返回 null，不抛出。</summary>
    private static SessionRecord? TryLoad(string path, List<SessionLoadError> errors)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var rec = JsonSerializer.Deserialize<SessionRecord>(json, JsonOpts);
            if (rec is null) { errors.Add(new SessionLoadError(path, "反序列化结果为 null")); return null; }
            if (rec.SessionId == Guid.Empty) { errors.Add(new SessionLoadError(path, "SessionId 为空")); return null; }
            if (rec.Turns is null) rec.Turns = new List<TurnRecord>();
            return rec;
        }
        catch (Exception ex)
        {
            errors.Add(new SessionLoadError(path, ex.GetType().Name + ": " + ex.Message));
            return null;
        }
    }

    private static TurnStatus ParseStatus(string? s) => s?.ToLowerInvariant() switch
    {
        "running" => TurnStatus.Running,
        "completed" => TurnStatus.Completed,
        "stopped" => TurnStatus.Stopped,
        "failed" => TurnStatus.Failed,
        _ => throw new ArgumentException($"未知的 status：{s}")
    };

    private static StopMode ParseStopMode(string? s) => s?.ToLowerInvariant() switch
    {
        "none" => StopMode.None,
        "immediate" => StopMode.Immediate,
        "safe" => StopMode.Safe,
        _ => throw new ArgumentException($"未知的 stopMode：{s}")
    };
}

// ==================== 自检 ====================

/// <summary>不依赖测试框架的轻量自检；Run 返回空数组表示全部通过。</summary>
internal static class SessionStoreSelfTest
{
    /// <summary>在 root 下的唯一临时子目录里跑一遍完整自检，结束后清理。</summary>
    public static string[] Run(string root)
    {
        var failures = new List<string>();
        var testRoot = Path.Combine(root, "selftest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(testRoot);
            RunInner(testRoot, failures);
        }
        catch (Exception ex)
        {
            failures.Add("自检抛出异常: " + ex);
        }
        finally
        {
            try { Directory.Delete(testRoot, recursive: true); } catch { /* 清理失败不影响结论 */ }
        }
        return failures.ToArray();
    }

    private static void RunInner(string root, List<string> failures)
    {
        var store = new SessionStore(root);
        var workDir = @"C:\SelfTestWork";

        // 1. 创建 + 读回
        var created = store.CreateSession(workDir, "自检会话");
        if (created.SessionId == Guid.Empty) failures.Add("CreateSession 返回空 SessionId");
        var loaded = store.LoadSession(created.SessionId);
        if (loaded is null) failures.Add("LoadSession 返回 null");
        else
        {
            if (loaded.Title != "自检会话") failures.Add("Title 不一致");
            if (loaded.WorkDir != workDir) failures.Add("WorkDir 不一致");
        }

        // 2. 完成轮次
        var turn = store.BeginTurn(created.SessionId, "你好");
        store.CompleteTurn(created.SessionId, turn.TurnId, "回复内容", new[] { "a.txt", "b.txt" }, "completed", "none");
        var after = store.LoadSession(created.SessionId);
        if (after is null) failures.Add("CompleteTurn 后 LoadSession 为 null");
        else if (after.Turns.Count != 1) failures.Add($"Turns 数量应为 1，实际 {after.Turns.Count}");
        else
        {
            var t = after.Turns[0];
            if (t.Reply != "回复内容") failures.Add("Reply 不一致");
            if (t.Status != TurnStatus.Completed) failures.Add("Status 不一致");
            if (t.StopMode != StopMode.None) failures.Add("StopMode 不一致");
            if (t.Files.Count != 2) failures.Add("Files 数量不一致");
            if (t.EndedAt is null) failures.Add("EndedAt 为空");
        }

        // 3. ListSessions 过滤
        var list = store.ListSessions(workDir);
        if (list.Count != 1) failures.Add($"ListSessions(workDir) 数量应为 1，实际 {list.Count}");

        // 4. 删除
        if (!store.DeleteSession(created.SessionId)) failures.Add("DeleteSession 返回 false");
        if (store.LoadSession(created.SessionId) is not null) failures.Add("删除后仍能加载");

        // 5. 损坏文件跳过
        var corrupt = Path.Combine(root, Guid.NewGuid().ToString("D") + ".json");
        File.WriteAllText(corrupt, "{ 这不是合法 JSON", new UTF8Encoding(false));
        var good = store.CreateSession(workDir, "good");
        var list2 = store.ListSessions();
        if (!list2.Any(s => s.SessionId == good.SessionId))
            failures.Add("损坏文件影响了正常会话的列举");
        if (store.LastErrors.Count == 0)
            failures.Add("损坏文件未记入 LastErrors");
        try { File.Delete(corrupt); } catch { }

        // 6. 并发写同一会话（10 个轮次先后创建，再并发完成）
        var conc = store.CreateSession(workDir, "并发测试");
        var turnIds = new List<Guid>();
        for (int i = 0; i < 10; i++)
            turnIds.Add(store.BeginTurn(conc.SessionId, $"prompt-{i}").TurnId);
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(() =>
                store.CompleteTurn(conc.SessionId, turnIds[idx], $"reply-{idx}", new[] { $"f-{idx}.txt" }, "completed", "none")));
        }
        try { Task.WaitAll(tasks.ToArray()); }
        catch (Exception ex) { failures.Add("并发完成轮次抛异常: " + ex.Message); }
        var final = store.LoadSession(conc.SessionId);
        if (final is null) failures.Add("并发后 LoadSession 为 null");
        else
        {
            if (final.Turns.Count != 10) failures.Add($"并发后 Turns 数量应为 10，实际 {final.Turns.Count}");
            if (final.Turns.Any(t => t.Status != TurnStatus.Completed))
                failures.Add("并发后存在未完成的轮次");
        }
    }
}
