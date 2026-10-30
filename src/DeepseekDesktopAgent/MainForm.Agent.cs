using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeepseekDesktopAgent;

public sealed partial class MainForm
{
    private async Task ChooseDirClickedAsync()
    {
        if (_busy) return;
        if (!await ChooseWorkDirAsync()) return;
        try
        {
            await EnsureBrowserAsync(CancellationToken.None);
            RefreshSystemPrompt();
            ShowMainPage(0);
            SetStatus("工作目录已选择，点击“开始 / 继续”开始对话");
        }
        catch (Exception ex)
        {
            ShowError("启动 DeepSeek 浏览器失败", ex);
        }
    }
    private async Task<bool> ChooseWorkDirAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.WorkDir) && Directory.Exists(_cfg.WorkDir))
        {
            var result = MessageBox.Show(this,
                "你想继续这个工作目录吗？\r\n" + _cfg.WorkDir,
                "继续工作", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes)
            {
                _lblWorkDir.Text = _cfg.WorkDir;
                return true;
            }
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Deepseek-Desktop-Agent 的工作目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_cfg.WorkDir) ? _cfg.WorkDir : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        _cfg.WorkDir = dialog.SelectedPath;
        ConfigService.Save(_cfg);
        _lblWorkDir.Text = _cfg.WorkDir;
        RefreshSystemPrompt();
        Prompt.EnsureSkills(_tempDir);
        SetStatus("工作目录：" + _cfg.WorkDir);
        return true;
    }
    private async Task StartFlowAsync()
    {
        if (_busy) return;

        if (string.IsNullOrWhiteSpace(_cfg.WorkDir) || !Directory.Exists(_cfg.WorkDir))
        {
            if (!await ChooseWorkDirAsync()) return;
        }

        try
        {
            SetStatus("正在准备会话...");
            await EnsureBrowserAsync(CancellationToken.None);
            if (_cdp == null) throw new InvalidOperationException("DeepSeek 调试连接未建立。");

            Prompt.EnsureSkills(_tempDir);
            var systemFile = WriteSystemFile(SystemPromptText());
            var files = new List<string> { systemFile };
            string message;

            if (_rbOld.Checked)
            {
                if (IsSessionUrl(_lastSessionUrl))
                {
                    SetStatus("正在打开上次会话...");
                    await _cdp.NavigateAsync(_lastSessionUrl);
                    var reopened = await _cdp.GetCurrentUrlAsync();
                    if (IsSessionUrl(reopened))
                    {
                        message = ContinuationMessage();
                    }
                    else
                    {
                        SetStatus("上次会话已失效，请重新选择");
                        using var picker = new SessionPickerForm(_cdp, reopened);
                        if (picker.ShowDialog(this) != DialogResult.OK) return;
                        if (picker.CreateNew)
                        {
                            await _cdp.NavigateAsync("https://chat.deepseek.com");
                            message = "system.txt";
                        }
                        else
                        {
                            await _cdp.NavigateAsync(picker.SelectedUrl);
                            message = ContinuationMessage();
                        }
                    }
                }
                else
                {
                    var current = await _cdp.GetCurrentUrlAsync();
                    using var picker = new SessionPickerForm(_cdp, current);
                    if (picker.ShowDialog(this) != DialogResult.OK) return;

                    if (picker.CreateNew)
                    {
                        await _cdp.NavigateAsync("https://chat.deepseek.com");
                        message = "system.txt";
                    }
                    else
                    {
                        await _cdp.NavigateAsync(picker.SelectedUrl);
                        message = ContinuationMessage();
                    }
                }
            }
            else
            {
                await _cdp.NavigateAsync("https://chat.deepseek.com");
                message = "system.txt";
            }

            var persistedUrl = await ReadPersistedSessionUrlAsync();
            if (persistedUrl.Length > 0)
            {
                _lastSessionUrl = persistedUrl;
                SaveLastSession();
            }
            _rbNew.Enabled = false;
            _rbOld.Enabled = false;
            _txtInput.Clear();
            _userMessageCount++;
            UpdateCounters();
            // 新对话：新建会话；旧对话：并入当前工作目录下最近的会话（ListSessions 已按 UpdatedAt 倒序）。
            if (_rbNew.Checked)
            {
                _sessionId = null;
            }
            else
            {
                var existing = _sessions.ListSessions(_cfg.WorkDir);
                _sessionId = existing.Count > 0 ? existing[0].SessionId : null;
            }
            _turnId = null;
            await RunConversationAsync(message, files);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            ShowError("启动会话失败", ex);
        }
        finally
        {
            _rbNew.Enabled = true;
            _rbOld.Enabled = true;
        }
    }
    /// <summary>
    /// 只返回“已经真正落盘”的会话地址：URL 中的会话 id 必须已出现在 DeepSeek 侧边栏列表里。
    /// 新建对话时浏览器会短暂使用临时 id，直接保存会导致下次打开提示“该对话不存在”。
    /// </summary>
    private async Task<string> ReadPersistedSessionUrlAsync(CancellationToken ct = default)
    {
        if (_cdp == null) return "";
        try
        {
            var url = await _cdp.EvaluateAsync(
                "(() => { const h = location.href; const m = h.match(/\\/a\\/chat\\/s\\/([0-9a-fA-F-]{20,})/); " +
                "if (!m) return ''; const hit = Array.from(document.querySelectorAll('a[href*=\"/a/chat/s/\"]'))" +
                ".some(a => (a.getAttribute('href') || '').includes(m[1])); return hit ? h : ''; })()", 5000, ct);
            return IsSessionUrl(url) ? url.Trim() : "";
        }
        catch { return ""; }
    }
    private string ContinuationMessage()
    {
        return "[工具][继续对话] 1.Agent 炸了，现已重新连接，并重新提交系统提示词。2.原临时文件夹已被清空，现临时文件夹为" +
               _tempDir + "。3.请询问用户是否继续，你才能调用工具。";
    }
    private string WriteSystemFile(string promptText)
    {
        var path = Path.Combine(_tempDir, "system.txt");
        File.WriteAllText(path, promptText, new UTF8Encoding(true));
        return path;
    }

    /// <summary>按当前工作目录重新生成系统提示词，只放进“详情”页，不再占用对话输入框。</summary>
    private void RefreshSystemPrompt()
    {
        if (string.IsNullOrWhiteSpace(_cfg.WorkDir)) return;
        _txtDetailPrompt.Text = Prompt.Build(_cfg.WorkDir, _tempDir);
    }

    /// <summary>取用于发送的系统提示词：优先复用详情页里的内容，缺失时重新生成。</summary>
    private string SystemPromptText()
    {
        var text = _txtDetailPrompt.Text;
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("# System Prompt", StringComparison.Ordinal))
        {
            RefreshSystemPrompt();
            text = _txtDetailPrompt.Text;
        }
        return text;
    }
    /// <summary>会话页发送入口：把输入内容接到所选会话上继续执行。</summary>
    private void TryContinueSession()
    {
        var node = SelectedSessionNode();
        if (node == null)
        {
            SetStatus("请先在左侧选择一个会话");
            return;
        }
        var text = _txtSessionInput.Text.Trim();
        if (text.Length == 0) return;
        _txtSessionInput.Clear();
        _ = ContinueSessionAsync(node, text);
    }

    /// <summary>
    /// 接续会话：切回该会话的工作目录，把上一轮的输入与结论作为上下文，
    /// 再走既有发送流程，保证队列、附件与工具调用行为完全一致。
    /// </summary>
    private async Task ContinueSessionAsync(SessionTreeNode node, string text)
    {
        if (_busy)
        {
            SetStatus("请先停止当前任务，再继续该会话");
            return;
        }

        _sessionId = node.SessionId;
        _turnId = null;
        if (!string.IsNullOrWhiteSpace(node.WorkDir) &&
            !string.Equals(node.WorkDir, _cfg.WorkDir, StringComparison.OrdinalIgnoreCase))
        {
            _cfg.WorkDir = node.WorkDir;
            ConfigService.Save(_cfg);
            _lblWorkDir.Text = node.WorkDir;
        }

        string? lastPrompt = null;
        string? lastReply = null;
        try
        {
            var record = _sessions.LoadSession(node.SessionId);
            if (record != null && record.Turns.Count > 0)
            {
                lastPrompt = record.Turns[^1].Prompt;
                lastReply = record.Turns[^1].Reply;
            }
        }
        catch { }

        var sb = new StringBuilder();
        sb.AppendLine("[继续对话] 在同一工作目录继续已有的会话。");
        if (lastPrompt != null) sb.AppendLine("上一轮任务：" + ClipForContext(lastPrompt, 800));
        if (lastReply != null) sb.AppendLine("上一轮结论：" + ClipForContext(lastReply, 1200));
        sb.AppendLine();
        sb.Append("用户新指令：" + text);

        _txtInput.Text = sb.ToString();
        ShowMainPage(0);
        await SendUserAsync();
    }

    private static string ClipForContext(string text, int max)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private async Task SendUserAsync()
    {
        // Agent 运行期间输入框保持可用：消息只入队，等本轮全部工作结束后再发送。
        if (_busy)
        {
            var queuedText = _txtInput.Text;
            var queuedFiles = _attachedFiles.ToList();
            if (string.IsNullOrWhiteSpace(queuedText) && queuedFiles.Count == 0) return;

            lock (_queueLock) _queue.Enqueue(new QueuedMessage(queuedText, queuedFiles));
            SyncRuntimePending();

            _txtInput.Clear();
            ClearAttachments();
            _userMessageCount++;
            UpdateCounters();
            SetStatus($"已加入消息队列（共 {QueueCount} 条），本轮全部结束后自动发送");
            UpdateQueueHint();
            UpdateInputPlaceholder();
            return;
        }

        if (_browser == null || _cdp == null || !_browser.IsAlive)
        {
            MessageBox.Show(this, "请先选择工作目录并启动 DeepSeek 浏览器。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var text = _txtInput.Text;
        var files = _attachedFiles.ToList();
        if (string.IsNullOrWhiteSpace(text) && files.Count == 0) return;

        _txtInput.Clear();
        ClearAttachments();
        _userMessageCount++;
        UpdateCounters();
        await RunConversationAsync(text, files);
    }
    private async Task RunConversationAsync(string message, List<string> files)
    {
        if (_cdp == null)
        {
            ShowError("发送失败", new InvalidOperationException("DeepSeek 调试连接未建立。"));
            return;
        }

        message = AppendRelevantSkills(message);
        var isSystemRound = string.Equals(message, "system.txt", StringComparison.Ordinal);
        var lastReply = string.Empty;
        var turnStatus = "Failed";
        var stopMode = "None";
        Guid? turnId = null;
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        SetBusy(true);
        _safeStopRequested = false;
        UpdateRuntime(AgentPhase.Thinking);
        var isFirstTurn = false;

        // ③-A：每个用户轮次落盘，进程崩溃后可由会话树与恢复协议接回。
        try
        {
            if (_sessionId == null)
            {
                var title = isSystemRound ? "会话 " + DateTime.Now.ToString("MM-dd HH:mm") : BuildSessionTitle(message);
                _sessionId = _sessions.CreateSession(_cfg.WorkDir, title).SessionId;
                isFirstTurn = !isSystemRound;
            }
            if (!isSystemRound)
            {
                turnId = _sessions.BeginTurn(_sessionId.Value, TrimForStore(message)).TurnId;
                _turnId = turnId;
                _runtimeState.SessionId = _sessionId;
                _runtimeState.TurnId = turnId;
                UpdateRuntime(AgentPhase.Thinking);
            }
        }
        catch { }

        try
        {
            // 刚急停过：等网页里那段回复真正生成结束再发消息，否则网页仍在生成会导致发送失败。
            if (_waitPageIdleAfterStop)
            {
                _waitPageIdleAfterStop = false;
                SetStatus("等待 DeepSeek 结束当前回复...");
                await _cdp.WaitForReplyIdleAsync(120000, cts.Token);
            }

            var reply = await SendAndReceiveAsync(message, files, cts.Token);
            lastReply = reply;
            ShowReply(reply);

            for (var hop = 0; hop < 60; hop++)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (_safeStopRequested)
                {
                    _safeStopRequested = false;
                    turnStatus = "Stopped";
                    stopMode = "Safe";
                    SetStatus("已安全停止：本轮不再继续调用工具");
                    ShowReply("[已安全停止] 当前工具已完成，本轮后续工具与追问均已取消。");
                    break;
                }
                var call = ToolProtocol.Parse(reply);
                if (call == null)
                {
                    if (ToolProtocol.HasMarkers(reply))
                    {
                        var hint = "!![工具][错误]!!\n" +
                                   "检测到关键词【_识别头_完全保留本行注释】，你可能想调用工具，但是工具无法识别代码。" +
                                   "如果确实要调用工具：请按格式重新输出一次；如果没有调用工具却收到此信息：请忽略。\n//[END]//";
                        reply = await SendAndReceiveAsync(hint, new List<string>(), cts.Token);
                        lastReply = reply;
                        ShowReply(reply);
                        continue;
                    }
                    turnStatus = "Completed";
                    break;
                }

                _toolCount++;
                UpdateCounters();
                SetStatus("正在执行工具：" + call.Kind);
                UpdateRuntime(AgentPhase.ToolRunning, call.Kind.ToString());


                ToolResult result;
                try
                {
                    result = await (_tools ?? throw new InvalidOperationException("工具执行器未初始化。")).ExecuteAsync(call);
                }
                catch (OperationCanceledException) { throw; }

                if (_safeStopRequested)
                {
                    _safeStopRequested = false;
                    turnStatus = "Stopped";
                    stopMode = "Safe";
                    SetStatus("已安全停止：当前工具已完成");
                    ShowReply("[已安全停止] 当前工具已执行完毕，结果不再回传，后续工具与追问均已取消。");
                    break;
                }

                SetStatus("正在等待 DeepSeek 回复...");
                reply = await SendAndReceiveAsync(result.Message, result.Files, cts.Token);
                lastReply = reply;
                ShowReply(reply);
            }

            await CaptureThinkingAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            turnStatus = "Stopped";
            stopMode = "Immediate";
            SetStatus("已急停");
            ShowReply("[已急停] 当前任务被用户终止，队列中的消息将在停止完成后发送。");
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // CDP 调用在取消时抛出的是 CdpException，同样按急停处理。
            turnStatus = "Stopped";
            stopMode = "Immediate";
            SetStatus("已急停");
            ShowReply("[已急停] 当前任务被用户终止，队列中的消息将在停止完成后发送。");
        }
        catch (Exception ex)
        {
            SetStatus("运行失败: " + ex.Message);
            ShowReply("!![运行错误]!!\n" + ex.Message);
            MessageBox.Show(this, ex.Message, "Deepseek-Desktop-Agent 运行错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (_runCts == cts) _runCts = null;
            if (turnId is { } finishedTurn && _sessionId is { } finishedSession)
            {
                try
                {
                    _sessions.CompleteTurn(finishedSession, finishedTurn, lastReply,
                        Array.Empty<string>(), turnStatus, stopMode);

                    // 首轮结束：用 prompt 去前缀 + 标点断句生成更干净的标题（本地规则，不污染对话）。
                    if (isFirstTurn && lastReply.Length > 0)
                    {
                        try
                        {
                            var newTitle = BuildSessionTitleV2(message, lastReply);
                            if (newTitle.Length > 0 && newTitle != "新会话")
                                _sessions.UpdateTitle(finishedSession, newTitle);
                        }
                        catch { }
                    }
                }
                catch { }
                _turnId = null;
                _runtimeState.TurnId = null;
                RunOnUi(RefreshSessions);
            }
            SetBusy(false);
            UpdateRuntime(AgentPhase.Idle);
            // 只有整轮全部结束（DeepSeek 已回复且所有工具调用完成）后才发送队列里的下一条消息。
            StartNextQueuedMessage();
        }
    }

    private static string BuildSessionTitle(string message)
    {
        var text = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0) return "新会话";
        return text.Length <= 24 ? text : text[..24] + "…";
    }

    /// <summary>
    /// 首轮结束后生成更干净的标题：去常见礼貌前缀、按句末/分句标点断句，限制 20 字。
    /// 纯本地规则，不调用 AI（避免污染对话历史）。
    /// </summary>
    private static string BuildSessionTitleV2(string prompt, string reply)
    {
        _ = reply;
        var text = StripPromptPrefix(prompt);
        if (text.Length == 0) return "新会话";

        // 句末标点优先：。？！!?
        for (var i = 0; i < text.Length && i < 40; i++)
        {
            var c = text[i];
            if (c is '。' or '？' or '！' or '!' or '?')
            {
                var head = text[..(i + 1)].Trim();
                return head.Length <= 20 ? head : head[..20];
            }
        }

        if (text.Length <= 20) return text;

        // 次选：15-30 字间按分句标点断
        for (var i = 15; i < text.Length && i < 30; i++)
        {
            var c = text[i];
            if (c is '，' or ',' or '、' or '；' or ';')
                return text[..i].TrimEnd('，', ',', '。', '；', ';', ' ') ;
        }
        return text[..20].TrimEnd('，', ',', '。', '；', ';', ' ') ;
    }

    /// <summary>去掉常见开头礼貌/请求前缀，返回精简后的主句。</summary>
    private static string StripPromptPrefix(string? raw)
    {
        var text = (raw ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0) return text;
        string[] prefixes =
        {
            "请帮我", "请你", "帮我", "麻烦", "我想", "我需要", "我想要",
            "能不能", "能否", "可不可以", "可以", "给我", "请"
        };
        foreach (var pre in prefixes)
        {
            if (text.StartsWith(pre, StringComparison.Ordinal))
            {
                text = text[pre.Length..].TrimStart('，', ',', ' ', '、', '：', ':');
                break;
            }
        }
        return text;
    }

    private static string TrimForStore(string text)
    {
        var value = text ?? string.Empty;
        return value.Length <= 4000 ? value : value[..4000] + "\n…（已截断）";
    }
    /// <summary>④ 用户在顶栏切换深度思考时，同步到网页按钮。</summary>
    private async Task ToggleDeepThinkAsync()
    {
        if (_syncingDeepThink || _cdp is not { IsConnected: true }) return;
        try
        {
            var wanted = _chkDeepThink.Checked;
            var on = await _cdp.SetDeepThinkAsync(wanted);
            if (on != wanted)
            {
                _syncingDeepThink = true;
                _chkDeepThink.Checked = !wanted;
                _syncingDeepThink = false;
                SetStatus("深度思考开关切换失败，请在网页端手动确认");
            }
        }
        catch { }
    }

    /// <summary>连接成功后把网页实际状态同步到顶栏，避免显示与页面不一致。</summary>
    private async Task SyncDeepThinkStateAsync()
    {
        if (_cdp is not { IsConnected: true }) return;
        try
        {
            var state = await _cdp.GetDeepThinkStateAsync();
            if (state == null) return;
            _syncingDeepThink = true;
            _chkDeepThink.Checked = state.Value;
            _syncingDeepThink = false;
        }
        catch
        {
            _syncingDeepThink = false;
        }
    }

    /// <summary>② 本轮结束后读取思考块，写入 PowerShell 面板日志，供事后查看。</summary>
    private async Task CaptureThinkingAsync(CancellationToken ct)
    {
        try
        {
            if (_cdp is not { IsConnected: true }) return;
            var block = await _cdp.ReadThinkingAsync(ct);
            if (block.IsEmpty) return;
            ToolOutput("【思考】" + ThinkingBlockParser.FormatSummary(block));
            if (block.Body.Length > 0) ToolOutput(block.Body);
        }
        catch { }
    }

    /// <summary>⑤-A：按本轮消息挑出相关技能正文，追加到发送内容末尾；无命中时原样返回。</summary>
    private string AppendRelevantSkills(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;
        if (message.Contains("## 相关技能", StringComparison.Ordinal)) return message;
        try
        {
            var zip = Path.Combine(_tempDir, "skill_all.zip");
            if (!File.Exists(zip)) return message;
            var result = new SkillInjector(zip).Build(message, 4000);
            return result.Text.Length == 0 ? message : message + "\n\n" + result.Text;
        }
        catch
        {
            return message;
        }
    }

    private void UpdateRuntime(AgentPhase phase, string? toolKind = null)
    {
        try
        {
            _runtimeState.Phase = phase;
            if (!string.IsNullOrWhiteSpace(toolKind))
            {
                _runtimeState.LastToolKind = toolKind!;
                _runtimeState.LastToolStartedAt = DateTimeOffset.Now;
            }
            _runtime.Save(_runtimeState);
        }
        catch { }
    }

    private void SyncRuntimePending()
    {
        try
        {
            lock (_queueLock) _runtimeState.PendingPrompts = _queue.Select(q => q.Text).ToList();
            _runtime.Save(_runtimeState);
        }
        catch { }
    }


    /// <summary>发送前加 300-1200ms 随机微抖动，模拟"组织语言后点击发送"的人为节奏。</summary>
    private static async Task HumanPauseAsync(CancellationToken ct)
    {
        var ms = Random.Shared.Next(300, 1201);
        await Task.Delay(ms, ct);
    }

    private async Task<string> SendAndReceiveAsync(string text, IReadOnlyList<string> files, CancellationToken ct)
    {
        if (_cdp == null) throw new InvalidOperationException("CDP 未连接。");

        // 发送前检查验证码
        if (await _cdp.CheckForCaptchaAsync(ct))
        {
            StopCurrent();
            RunOnUi(() =>
            {
                _txtInput.Enabled = false;
                _btnSend.Enabled = false;
                _btnStart.Enabled = false;
                SetStatus("检测到验证码，已锁定输入。请去设置页解锁。");
                MessageBox.Show(this, "检测到 DeepSeek 弹出验证码！\r\n为保护账号，输入框已锁定。\r\n请手动在网页完成验证后，前往“设置 -> 快捷操作”解除锁定。", "安全拦截", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            throw new OperationCanceledException("检查到验证码，已安全拦截并锁定。");
        }

        if (_chkAuto.Checked)
        {
            var min = _numDelayMin.Value;
            var max = Math.Max(min, _numDelayMax.Value);
            var delay = min == max ? min : Random.Shared.Next(min, max + 1);
            for (var i = delay; i > 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                SetStatus($"防封控延时 {i} 秒...");
                await Task.Delay(1000, ct);
            }
        }

        SetStatus("正在发送并等待 DeepSeek 回复...");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await HumanPauseAsync(ct);
            var reply = await _cdp.SendMessageAsync(text, files, 180000, ct);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                var persisted = await ReadPersistedSessionUrlAsync(ct);
                if (persisted.Length > 0)
                {
                    _lastSessionUrl = persisted;
                    SaveLastSession();
                }
                SetStatus("已收到回复");
                return reply;
            }
            SetStatus("未取回回复，正在重试...");
            await Task.Delay(1500, ct);
        }

        throw new CdpException("未取回 AI 回复。请检查 DeepSeek 页面是否正常、是否已登录。");
    }
    private void ShowReply(string reply)
    {
        _lastReplyRaw = reply;
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_last_reply.md"),
                reply, new UTF8Encoding(false));
        }
        catch { }
        RunOnUi(() =>
        {
            RenderReply();
            _txtReply.SelectionStart = _txtReply.TextLength;
            _txtReply.ScrollToCaret();
        });
    }
    private void RenderReply()
    {
        if (string.IsNullOrEmpty(_lastReplyRaw))
        {
            _txtReply.Clear();
            // 空态用文本框自身的文字提示，避免原生窗口遮挡 Label。
            _txtReply.Text = "会话回复会显示在这里\r\n输入任务后按 Ctrl+Enter 发送";
            _txtReply.SelectAll();
            _txtReply.SelectionColor = ThemeManager.Current.SubtleText;
            _txtReply.SelectionStart = 0;
            _txtReply.SelectionLength = 0;
            return;
        }

        try
        {
            _txtReply.Rtf = MarkdownRenderer.ToRtf(_lastReplyRaw, ThemeManager.Current);
        }
        catch (Exception ex)
        {
            AppLog.Write("Markdown 渲染失败: " + ex.Message);
            _txtReply.Text = _lastReplyRaw;
        }
    }
    private async Task SwitchSessionAsync()
    {
        if (_busy)
        {
            SetStatus("请先停止当前任务，再切换会话");
            return;
        }
        if (_cdp == null || _browser == null || !_browser.IsAlive)
        {
            MessageBox.Show(this, "请先启动 DeepSeek 浏览器。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var current = await _cdp.GetCurrentUrlAsync();
            using var picker = new SessionPickerForm(_cdp, current);
            if (picker.ShowDialog(this) != DialogResult.OK) return;

            if (picker.CreateNew)
            {
                await _cdp.NavigateAsync("https://chat.deepseek.com");
                _rbNew.Checked = true;
                var systemFile = WriteSystemFile(SystemPromptText());
                _userMessageCount++;
                UpdateCounters();
                await RunConversationAsync("system.txt", new List<string> { systemFile });
            }
            else
            {
                await _cdp.NavigateAsync(picker.SelectedUrl);
                _rbOld.Checked = true;
                _lastSessionUrl = picker.SelectedUrl;
                SaveLastSession();
                var systemFile = WriteSystemFile(SystemPromptText());
                _userMessageCount++;
                UpdateCounters();
                await RunConversationAsync(ContinuationMessage(), new List<string> { systemFile });
            }
        }
        catch (Exception ex)
        {
            ShowError("切换会话失败", ex);
        }
    }
    /// <summary>急停：立刻打断本轮 Agent 运行（含正在执行的工具与等待回复）。</summary>
    private void StopCurrent()
    {
        if (_runCts == null) return;
        SetStatus("正在急停...");
        _waitPageIdleAfterStop = true;
        try { _runCts.Cancel(); } catch { }
        _ps.KillCurrent();
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        RunOnUi(() =>
        {
            _btnStart.Enabled = !busy;
            _btnChooseDir.Enabled = !busy;
            _btnStop.Enabled = busy;
            _btnSwitch.Enabled = !busy;
            _rbNew.Enabled = !busy;
            _rbOld.Enabled = !busy;
            ShowThinking(busy);
            UpdateInputPlaceholder();
            ApplyQueueHint();
        });
    }

    private int QueueCount
    {
        get { lock (_queueLock) return _queue.Count; }
    }
    /// <summary>把队列里的下一条消息作为新的一轮发送。</summary>
    private void StartNextQueuedMessage()
    {
        if (_shuttingDown) return;

        QueuedMessage? next;
        lock (_queueLock)
        {
            next = _queue.Count > 0 ? _queue.Dequeue() : null;
        }
        SyncRuntimePending();
        ApplyQueueHint();
        if (next == null) return;

        SetStatus("正在发送队列中的消息...");
        _ = RunConversationAsync(next.Text, next.Files);
    }

    private void UpdateQueueHint() => RunOnUi(ApplyQueueHint);
    private void ApplyQueueHint()
    {
        var count = QueueCount;
        string hint;
        if (count > 0)
            hint = $"已排队 {count} 条消息，Agent 结束本轮全部工作后按顺序自动发送";
        else if (_busy)
            hint = "Agent 正在运行：现在输入的消息会排队，本轮全部结束后发送 · Esc 急停";
        else
            hint = "";

        _lblInputHint.Text = hint;
        var show = hint.Length > 0;
        _lblInputHint.Visible = show;
        if (_lblInputHint.Parent is TableLayoutPanel table && table.RowStyles.Count > 6)
            table.RowStyles[6].Height = show ? 22 : 0;
        RefreshQueuePreview();
    }

    /// <summary>切换队列预览的展开/折叠状态。</summary>
        /// <summary>按 Id 读取队列文本；不存在返回 null。</summary>
    private string? GetQueuedTextById(Guid id)
    {
        lock (_queueLock)
        {
            var item = _queue.FirstOrDefault(q => q.Id == id);
            return item?.Text;
        }
    }

    /// <summary>按 Id 修改队列文本；不存在返回 false，不改任何东西。</summary>
    private bool UpdateQueuedTextById(Guid id, string newText)
    {
        lock (_queueLock)
        {
            var item = _queue.FirstOrDefault(q => q.Id == id);
            if (item == null) return false;
            item.Text = newText ?? string.Empty;
        }
        SyncRuntimePending();
        UpdateQueueHint();
        return true;
    }

    /// <summary>按 Id 删除队列项；不存在返回 false。</summary>
    private bool RemoveQueuedById(Guid id)
    {
        lock (_queueLock)
        {
            if (!_queue.Any(q => q.Id == id)) return false;
            var list = _queue.Where(q => q.Id != id).ToList();
            _queue.Clear();
            foreach (var m in list) _queue.Enqueue(m);
        }
        SyncRuntimePending();
        UpdateQueueHint();
        return true;
    }

    /// <summary>弹出小对话框编辑队列消息文本；取消返回 null。</summary>
    private string? PromptEditQueuedText(string current)
    {
        using var dlg = new Form
        {
            Text = "编辑排队消息",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = false,
            Width = 560,
            Height = 260,
            ShowInTaskbar = false,
            BackColor = ThemeManager.Current.Background,
            ForeColor = ThemeManager.Current.Text
        };
        var tb = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Text = current ?? string.Empty,
            BackColor = ThemeManager.Current.Input,
            ForeColor = ThemeManager.Current.Text
        };
        var okBtn = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 88, Height = 32 };
        var cancelBtn = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 88, Height = 32 };
        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(6)
        };
        btnFlow.Controls.Add(cancelBtn);
        btnFlow.Controls.Add(okBtn);
        dlg.Controls.Add(tb);
        dlg.Controls.Add(btnFlow);
        dlg.AcceptButton = okBtn;
        dlg.CancelButton = cancelBtn;
        return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
    }

private void ToggleQueuePreview()
    {
        _queuePreviewExpanded = !_queuePreviewExpanded;
        RefreshQueuePreview();
    }

    /// <summary>根据当前队列内容刷新折叠条：0 条时隐藏；折叠时仅显示计数，展开时列每条消息。
    /// 展开最多显示 8 条，超出显示“还有 N 条”。由 ApplyQueueHint 与 Header 点击共同触发。</summary>
    private void RefreshQueuePreview()
    {
        if (_queuePreviewPanel == null || _queuePreviewPanel.IsDisposed) return;

        var items = new List<QueuedMessage>();
        lock (_queueLock) items.AddRange(_queue);
        var count = items.Count;

        if (count == 0)
        {
            _queuePreviewExpanded = false;
            _queuePreviewPanel.Visible = false;
            _queuePreviewList.Controls.Clear();
            if (_convoLayout != null && _convoLayout.RowStyles.Count > 4)
                _convoLayout.RowStyles[4].Height = 0;
            return;
        }

        _queuePreviewPanel.Visible = true;
        _queuePreviewHeader.Text = _queuePreviewExpanded
            ? $"消息队列（{count} 条）▾"
            : $"消息队列（{count} 条）▸";
        _queuePreviewList.Visible = _queuePreviewExpanded;

        _queuePreviewList.SuspendLayout();
        _queuePreviewList.Controls.Clear();
        if (_queuePreviewExpanded)
        {
            var max = Math.Min(count, 8);
            for (var i = 0; i < max; i++)
            {
                var m = items[i];
                var text = m.Text ?? string.Empty;
                text = text.Replace('\r', ' ').Replace('\n', ' ');
                if (text.Length > 60) text = text[..60] + "…";
                var attach = m.Files != null && m.Files.Count > 0 ? $"  [{m.Files.Count} 个附件]" : "";
                var row = new TableLayoutPanel
                {
                    ColumnCount = 3,
                    RowCount = 1,
                    Width = Math.Max(220, _queuePreviewList.ClientSize.Width - 24),
                    Height = 22,
                    Margin = new Padding(0, 0, 0, 2)
                };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

                var lbl = new Label
                {
                    Text = $"{i + 1}. {text}{attach}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Font = new Font("Microsoft YaHei UI", 8.25F),
                    ForeColor = ThemeManager.Current.SubtleText,
                    Margin = Padding.Empty
                };

                var capturedId = items[i].Id;
                var editBtn = new Button
                {
                    Text = "编辑",
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Microsoft YaHei UI", 7.5F),
                    Margin = new Padding(2, 1, 2, 1),
                    ForeColor = ThemeManager.Current.SubtleText
                };
                editBtn.FlatAppearance.BorderSize = 0;
                editBtn.Click += (_, _) =>
                {
                    var cur = GetQueuedTextById(capturedId);
                    if (cur == null) return;
                    var edited = PromptEditQueuedText(cur);
                    if (edited == null) return;
                    UpdateQueuedTextById(capturedId, edited);
                };

                var delBtn = new Button
                {
                    Text = "删除",
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Microsoft YaHei UI", 7.5F),
                    Margin = new Padding(2, 1, 2, 1),
                    ForeColor = ThemeManager.Current.SubtleText
                };
                delBtn.FlatAppearance.BorderSize = 0;
                delBtn.Click += (_, _) => RemoveQueuedById(capturedId);

                row.Controls.Add(lbl, 0, 0);
                row.Controls.Add(editBtn, 1, 0);
                row.Controls.Add(delBtn, 2, 0);
                _queuePreviewList.Controls.Add(row);
            }
            if (count > max)
            {
                _queuePreviewList.Controls.Add(new Label
                {
                    Text = $"…还有 {count - max} 条",
                    AutoSize = false,
                    Width = Math.Max(200, _queuePreviewList.ClientSize.Width - 24),
                    Height = 20,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Italic),
                    ForeColor = ThemeManager.Current.SubtleText,
                    Margin = new Padding(0, 0, 0, 2)
                });
            }
        }
        _queuePreviewList.ResumeLayout();

        if (_convoLayout != null && _convoLayout.RowStyles.Count > 4)
        {
            var visibleCount = _queuePreviewExpanded ? Math.Min(count, 8) : 0;
            var h = 28 + visibleCount * 24;
            if (_queuePreviewExpanded && count > 8) h += 24;
            _convoLayout.RowStyles[4].Height = h;
        }
    }
    /// <summary>剪贴板里有图片时存成 PNG 并加入附件；返回是否已按图片处理。</summary>
    private bool TryPasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return false;
            var image = Clipboard.GetImage();
            if (image == null) return false;

            Directory.CreateDirectory(_tempDir);
            var file = Path.Combine(_tempDir, "粘贴图片_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            image.Save(file, ImageFormat.Png);
            image.Dispose();

            if (!_attachedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                _attachedFiles.Add(file);
            UpdateAttachLabel();
            SetStatus("已粘贴图片作为附件：" + Path.GetFileName(file));
            return true;
        }
        catch (Exception ex)
        {
            ShowError("粘贴图片失败", ex);
            return true;
        }
    }

    private void AttachFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要附加的文件" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames)
        {
            if (!_attachedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                _attachedFiles.Add(file);
        }
        UpdateAttachLabel();
    }
    private void ClearAttachments()
    {
        _attachedFiles.Clear();
        UpdateAttachLabel();
    }
    private void UpdateAttachLabel()
    {
        _lblAttach.Text = _attachedFiles.Count == 0
            ? "未选择附件"
            : $"已附加 {_attachedFiles.Count} 个文件：" + string.Join("; ", _attachedFiles);
        _lblAttach.ForeColor = _attachedFiles.Count == 0 ? ThemeManager.Current.SubtleText : ThemeManager.Current.Accent;
        // 没有附件时整行收起，避免主页常驻一行无意义提示。
        if (_convoLayout.RowStyles.Count > 3) _convoLayout.RowStyles[3].Height = _attachedFiles.Count == 0 ? 0 : 22;
    }
    private void SaveDelay()
    {
        if (_savingDelay) return;
        _savingDelay = true;
        try
        {
            _numDelayMin.Commit();
            _numDelayMax.Commit();
            if (_numDelayMax.Value < _numDelayMin.Value) _numDelayMax.Value = _numDelayMin.Value;
            _cfg.DelayMinSeconds = _numDelayMin.Value;
            _cfg.DelayMaxSeconds = Math.Max(_numDelayMin.Value, _numDelayMax.Value);
            ConfigService.Save(_cfg);
        }
        finally
        {
            _savingDelay = false;
        }
    }
    /// <summary>DeepSeek 会话页地址，形如 https://chat.deepseek.com/a/chat/s/&lt;id&gt;。</summary>
    private static bool IsSessionUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("/a/chat/s/", StringComparison.OrdinalIgnoreCase);
    private void SaveLastSession()
    {
        if (!IsSessionUrl(_lastSessionUrl)) return;
        _cfg.LastSessionUrl = _lastSessionUrl;
        ConfigService.Save(_cfg);
    }
    private void ResetConfig()
    {
        if (MessageBox.Show(this, "确定要重置所有配置吗？", "Deepseek-Desktop-Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _cfg = new AppConfig();
        ConfigService.Save(_cfg);
        _chkNight.Checked = false;
        _chkAuto.Checked = false;
        _chkEnterSend.Checked = true;
        _numDelayMin.Value = _cfg.DelayMinSeconds;
        _numDelayMax.Value = _cfg.DelayMaxSeconds;
        _trkOpacity.Value = _cfg.OpacityPercent;
        _lblWorkDir.Text = "未选择工作目录";
        _txtInput.Clear();
        ApplyPalette();
        SetStatus("配置已重置");
    }
    private void ClearRunLog()
    {
        _lstRuns.Items.Clear();
        _txtPsCode.Clear();
        _txtPsOut.Clear();
        _currentRunIndex = -1;
        _lblPsState.Text = "状态：空闲";
    }
}
