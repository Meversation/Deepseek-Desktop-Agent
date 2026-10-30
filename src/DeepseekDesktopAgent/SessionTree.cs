using System.Globalization;

namespace DeepseekDesktopAgent;

/// <summary>会话树里的单个会话节点。</summary>
public sealed class SessionTreeNode
{
    public Guid SessionId { get; init; }
    public string? Title { get; init; }
    public string WorkDir { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int TurnCount { get; init; }

    /// <summary>没有标题时用于界面展示的占位名，不回写存储。</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title) ? "未命名会话" : ReadableTitle(Title!);

    /// <summary>
    /// 续接轮次的标题直接取自注入的续接提示词，开头固定是“[工具][继续对话]”，
    /// 列表里会全部长得一样；这里去掉这些标记并压缩空白，只留可读内容。
    /// </summary>
    private static string ReadableTitle(string title)
    {
        var text = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (true)
        {
            var trimmed = text;
            foreach (var marker in Markers)
                if (trimmed.StartsWith(marker, StringComparison.Ordinal)) trimmed = trimmed[marker.Length..].Trim();
            if (trimmed.Length == text.Length) break;
            text = trimmed;
        }

        while (text.Contains("  ", StringComparison.Ordinal)) text = text.Replace("  ", " ");
        return text.Length == 0 ? "未命名会话" : text;
    }

    private static readonly string[] Markers = { "[工具]", "[继续对话]", "【工具】", "【继续对话】" };

    public string UpdatedText => UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>按工作目录聚合的一组会话。</summary>
public sealed class WorkDirNode
{
    public string WorkDir { get; init; } = string.Empty;
    public IReadOnlyList<SessionTreeNode> Sessions { get; init; } = Array.Empty<SessionTreeNode>();

    public int SessionCount => Sessions.Count;
    public int TotalTurns => Sessions.Sum(s => s.TurnCount);

    /// <summary>组内最近一次活动时间；空组返回 DateTimeOffset.MinValue。</summary>
    public DateTimeOffset LastActiveAt =>
        Sessions.Count == 0 ? DateTimeOffset.MinValue : Sessions.Max(s => s.UpdatedAt);
}

/// <summary>会话树快照：每次构建产生一份不可变结果。</summary>
public sealed class SessionTree
{
    public IReadOnlyList<WorkDirNode> Groups { get; init; } = Array.Empty<WorkDirNode>();
    public int SessionCount => Groups.Sum(g => g.SessionCount);
    public int TurnCount => Groups.Sum(g => g.TotalTurns);
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>把 SessionStore 的扁平摘要整理成“工作目录 → 会话”的树，供界面直接绑定。</summary>
public sealed class SessionTreeService
{
    private readonly SessionStore _store;

    public SessionTreeService(SessionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>构建会话树；filter 为空不过滤，否则按标题与工作目录做忽略大小写的包含匹配。</summary>
    public SessionTree Build(string? filter = null)
    {
        var keyword = filter?.Trim() ?? string.Empty;
        var summaries = _store.ListSessions() ?? Array.Empty<SessionSummary>();

        var matched = new List<SessionSummary>();
        foreach (var item in summaries)
        {
            if (keyword.Length > 0 && !Matches(item, keyword)) continue;
            matched.Add(item);
        }

        // 按工作目录分组：路径比较忽略大小写，展示时保留首次出现的写法。
        var groups = new Dictionary<string, List<SessionSummary>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in matched)
        {
            var key = item.WorkDir ?? string.Empty;
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<SessionSummary>();
                groups[key] = list;
            }
            list.Add(item);
        }

        var nodes = new List<WorkDirNode>(groups.Count);
        foreach (var pair in groups)
        {
            var sessions = pair.Value
                .OrderByDescending(s => s.UpdatedAt)
                .ThenBy(s => s.SessionId)
                .Select(s => new SessionTreeNode
                {
                    SessionId = s.SessionId,
                    Title = s.Title,
                    WorkDir = s.WorkDir ?? pair.Key,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    TurnCount = s.TurnCount
                })
                .ToList();

            nodes.Add(new WorkDirNode { WorkDir = pair.Key, Sessions = sessions });
        }

        nodes.Sort(static (a, b) =>
        {
            var byTime = b.LastActiveAt.CompareTo(a.LastActiveAt);
            return byTime != 0 ? byTime : string.Compare(a.WorkDir, b.WorkDir, StringComparison.OrdinalIgnoreCase);
        });

        return new SessionTree { Groups = nodes };
    }

    private static bool Matches(SessionSummary item, string keyword)
    {
        if (item.WorkDir != null && item.WorkDir.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        var title = string.IsNullOrWhiteSpace(item.Title) ? "未命名会话" : item.Title!;
        return title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SessionTreeSelfTest
{
    public static string[] Run(string root)
    {
        var failures = new List<string>();
        var testRoot = Path.Combine(root, "tree_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionStore(testRoot);
            var a = store.CreateSession(@"C:\Work\Alpha", "重构登录");
            var b = store.CreateSession(@"C:\Work\Alpha", "修复滚动条");
            var c = store.CreateSession(@"c:\work\alpha", "小写路径");
            var d = store.CreateSession(@"D:\Work\Beta");
            var e = store.CreateSession(@"D:\Work\Beta", "数据导出");
            var f = store.CreateSession(@"E:\Work\Gamma", "PowerShell 脚本");

            // 给部分会话追加轮次，用于验证总轮数与排序。
            var turn1 = store.BeginTurn(a.SessionId, "第一轮");
            store.CompleteTurn(a.SessionId, turn1.TurnId, "完成", Array.Empty<string>(), "Completed", "None");
            var turn2 = store.BeginTurn(a.SessionId, "第二轮");
            store.CompleteTurn(a.SessionId, turn2.TurnId, "完成", Array.Empty<string>(), "Completed", "None");
            var turn3 = store.BeginTurn(e.SessionId, "导出");
            store.CompleteTurn(e.SessionId, turn3.TurnId, "完成", Array.Empty<string>(), "Completed", "None");

            var service = new SessionTreeService(store);
            var tree = service.Build();

            if (tree.SessionCount != 6) failures.Add($"会话数应为 6，实际 {tree.SessionCount}");
            if (tree.Groups.Count != 3) failures.Add($"分组数应为 3，实际 {tree.Groups.Count}");
            if (tree.TurnCount != 3) failures.Add($"总轮数应为 3，实际 {tree.TurnCount}");

            var alpha = tree.Groups.FirstOrDefault(g => g.WorkDir.Contains("Alpha", StringComparison.OrdinalIgnoreCase));
            if (alpha == null) failures.Add("缺少 Alpha 分组");
            else
            {
                if (alpha.SessionCount != 3) failures.Add($"Alpha 会话数应为 3，实际 {alpha.SessionCount}");
                if (alpha.TotalTurns != 2) failures.Add($"Alpha 总轮数应为 2，实际 {alpha.TotalTurns}");
                var ordered = alpha.Sessions.Zip(alpha.Sessions.Skip(1), (x, y) => x.UpdatedAt >= y.UpdatedAt).All(v => v);
                if (!ordered) failures.Add("Alpha 组内未按更新时间降序");
            }

            var sortedGroups = tree.Groups.Select(g => g.LastActiveAt).ToList();
            for (var i = 1; i < sortedGroups.Count; i++)
            {
                if (sortedGroups[i - 1] < sortedGroups[i])
                {
                    failures.Add("分组未按最近活动时间降序");
                    break;
                }
            }

            var filtered = service.Build("修复");
            if (filtered.SessionCount != 1) failures.Add($"过滤“修复”应命中 1 条，实际 {filtered.SessionCount}");
            if (filtered.Groups.Any(g => g.SessionCount == 0)) failures.Add("过滤后出现空分组");

            var byDir = service.Build("beta");
            if (byDir.SessionCount != 2) failures.Add($"按目录过滤应命中 2 条，实际 {byDir.SessionCount}");

            var blank = service.Build("   ");
            if (blank.SessionCount != 6) failures.Add("空白过滤不应生效");

            var emptyTree = new SessionTreeService(new SessionStore(Path.Combine(testRoot, "missing"))).Build();
            if (!emptyTree.IsEmpty) failures.Add("空目录应返回空树");

            var noTitle = service.Build("未命名").Groups
                .SelectMany(g => g.Sessions)
                .FirstOrDefault(s => s.SessionId == d.SessionId);
            if (noTitle == null) failures.Add("未命名会话过滤失败");
            else if (noTitle.DisplayTitle != "未命名会话") failures.Add("未命名会话占位标题错误");

            if (store.LoadSession(d.SessionId)?.Title != null)
                failures.Add("占位标题不应写回存储");

            // 续接提示词里的固定标记要去掉，否则列表里每条都显示成一模一样的前缀。
            var messy = new SessionTreeNode
            {
                SessionId = Guid.NewGuid(),
                Title = "[工具][继续对话] 1.Agent 炸了，现已重新连接",
                WorkDir = @"C:\Work\Alpha",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            if (messy.DisplayTitle != "1.Agent 炸了，现已重新连接")
                failures.Add("标题前缀清洗错误：" + messy.DisplayTitle);
        }
        catch (Exception ex)
        {
            failures.Add("会话树自检异常: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
        }
        return failures.ToArray();
    }
}
