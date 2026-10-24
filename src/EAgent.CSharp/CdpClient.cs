using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EAgent;

public sealed class CdpException : Exception
{
    public CdpException(string message) : base(message) { }
    public CdpException(string message, Exception inner) : base(message, inner) { }
}

public sealed class CdpClient : IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private ClientWebSocket _ws = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private int _nextId;
    private bool _disposing;

    public int Port { get; private set; } = 9222;
    public bool IsConnected => _ws.State == WebSocketState.Open;
    public event Action<string>? Log;

    private const string KeySelectorJs = """
        function pick(sel, root) {
          try { return (root || document).querySelectorAll(sel); } catch (e) { return []; }
        }
        var Sel = {
          input: 'textarea',
          assistant: '.ds-assistant-message-main-content, [class*="ds-assistant-message-main-content"]',
          send: 'div[role="button"].ds-button--primary.ds-button--circle, [role="button"][aria-label*="发送"], [role="button"][aria-label*="Send"]',
          file: 'input[type="file"]'
        };
        """;

    private const string LastAssistantJs = KeySelectorJs + """
        function lastAssistant() {
          var els = pick(Sel.assistant);
          if (!els || els.length === 0) return { count: 0, text: '' };
          var last = els[els.length - 1];
          var text = (last.textContent || '').trim();
          return { count: els.length, text: text };
        }
        JSON.stringify(lastAssistant())
        """;

    public async Task ConnectAsync(int port = 9222, CancellationToken ct = default)
    {
        Port = port;
        await DisposeSocketAsync(clearPending: true);
        _disposing = false;
        _ws = new ClientWebSocket();

        var wsUrl = await FindPageSocketAsync(port, ct);
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        try
        {
            await _ws.ConnectAsync(new Uri(wsUrl), ct);
        }
        catch (Exception ex)
        {
            throw new CdpException("无法连接 DeepSeek 调试页面，请确认浏览器已启动。", ex);
        }

        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);

        try
        {
            await CallAsync("Page.enable", null, 5000, ct);
            await CallAsync("Page.bringToFront", null, 5000, ct);
            await CallAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true }, 5000, ct);
        }
        catch (Exception ex)
        {
            Log?.Invoke("CDP 初始化警告: " + ex.Message);
        }
    }

    private async Task<string> FindPageSocketAsync(int port, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 0; i < 30; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", ct);
                using var doc = JsonDocument.Parse(json);
                JsonElement? fallback = null;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                    if (!el.TryGetProperty("webSocketDebuggerUrl", out var wsEl)) continue;
                    var url = el.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                    if (url.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase))
                        return wsEl.GetString()!;
                    fallback ??= el.Clone();
                }
                if (fallback != null)
                {
                    var ws = fallback.Value.GetProperty("webSocketDebuggerUrl").GetString();
                    if (!string.IsNullOrWhiteSpace(ws)) return ws;
                }
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(500, ct);
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{port}/json/new?https://chat.deepseek.com");
            using var resp = await Http.SendAsync(req, ct);
            await Task.Delay(1200, ct);
            return await FindPageSocketAsync(port, ct);
        }
        catch (Exception ex) { last = ex; }

        throw new CdpException("未找到可用的 DeepSeek 页面。请确认浏览器已登录并打开 chat.deepseek.com。", last ?? new Exception("unknown"));
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new CdpException("CDP 连接已关闭");
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (ms.Length == 0) continue;
                var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                HandleMessage(json);
            }
        }
        catch (Exception ex)
        {
            if (!_disposing) FailPending(ex);
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) return;
            if (!_pending.TryRemove(id, out var tcs)) return;
            tcs.TrySetResult(root.Clone());
        }
        catch { }
    }

    private void FailPending(Exception ex)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private async Task<JsonElement?> CallAsync(string method, JsonObject? parameters, int timeoutMs, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open)
            throw new CdpException("CDP 未连接");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters != null) payload["params"] = parameters;
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());

        try
        {
            await _sendLock.WaitAsync(ct);
            try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            throw new CdpException($"发送 CDP 命令失败: {method}", ex);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            JsonElement root;
            try { root = await tcs.Task.WaitAsync(timeoutCts.Token); }
            catch (OperationCanceledException)
            {
                throw new CdpException(ct.IsCancellationRequested
                    ? "操作已取消"
                    : $"CDP 命令超时: {method}");
            }

            if (root.TryGetProperty("error", out var error))
                throw new CdpException($"CDP 错误({method}): {error.GetRawText()}");
            return root.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task<string> EvaluateAsync(string expression, int timeoutMs = 10000, CancellationToken ct = default)
    {
        var result = await CallAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true
        }, timeoutMs, ct);

        if (result == null) return "";
        if (result.Value.TryGetProperty("exceptionDetails", out var ex))
            throw new CdpException("页面脚本执行异常: " + ex.GetRawText());

        if (!result.Value.TryGetProperty("result", out var remote)) return "";
        if (remote.TryGetProperty("value", out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.GetRawText()
            };
        }
        if (remote.TryGetProperty("description", out var desc)) return desc.GetString() ?? "";
        return "";
    }

    private async Task<string?> EvaluateObjectIdAsync(string expression, CancellationToken ct)
    {
        var result = await CallAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = false
        }, 10000, ct);
        if (result == null) return null;
        if (!result.Value.TryGetProperty("result", out var remote)) return null;
        return remote.TryGetProperty("objectId", out var oid) ? oid.GetString() : null;
    }

    public async Task<string> GetCurrentUrlAsync(CancellationToken ct = default)
    {
        try { return await EvaluateAsync("location.href", 5000, ct); }
        catch { return ""; }
    }

    public async Task WaitReadyAsync(int timeoutMs = 30000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var state = await EvaluateAsync(
                    "JSON.stringify({ready:document.readyState, hasInput:!!document.querySelector('textarea')})",
                    5000, ct);
                using var doc = JsonDocument.Parse(state);
                if (doc.RootElement.GetProperty("ready").GetString() == "complete" &&
                    doc.RootElement.GetProperty("hasInput").GetBoolean())
                {
                    await Task.Delay(1200, ct);
                    return;
                }
            }
            catch (CdpException) { }
            await Task.Delay(500, ct);
        }
    }

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        await CallAsync("Page.navigate", new JsonObject { ["url"] = url }, 15000, ct);
        await WaitReadyAsync(30000, ct);
        try
        {
            await CallAsync("Page.bringToFront", null, 5000, ct);
            await CallAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true }, 5000, ct);
        }
        catch { }
    }

    public async Task<string> SendMessageAsync(string message, IReadOnlyList<string> files, int timeoutMs, CancellationToken ct)
    {
        if (!IsConnected)
            await ConnectAsync(Port, ct);

        try { await CallAsync("Page.bringToFront", null, 5000, ct); } catch { }
        try { await CallAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true }, 5000, ct); } catch { }

        if (files.Count > 0)
            await UploadFilesAsync(files, ct);

        if (!string.IsNullOrEmpty(message))
        {
            var inject = KeySelectorJs + """
                (function(msg){
                  var ta = document.querySelector(Sel.input);
                  if (!ta) return 'NO_INPUT';
                  var proto = Object.getPrototypeOf(ta);
                  var desc = Object.getOwnPropertyDescriptor(proto, 'value');
                  if (desc && desc.set) desc.set.call(ta, msg); else ta.value = msg;
                  ta.focus();
                  ta.dispatchEvent(new Event('input', { bubbles: true }));
                  ta.dispatchEvent(new Event('change', { bubbles: true }));
                  return 'OK';
                })(
                """ + JsonSerializer.Serialize(message) + ")";
            var setResult = await EvaluateAsync(inject, 10000, ct);
            if (setResult == "NO_INPUT") throw new CdpException("未找到 DeepSeek 输入框，请确认页面已加载。");
        }

        var before = await ReadReplySnapshotAsync(ct);

        if (!string.IsNullOrEmpty(message))
        {
            await Task.Delay(300, ct);
            await EvaluateAsync(KeySelectorJs + """
                (function(){
                  var ta = document.querySelector(Sel.input);
                  if (!ta) return false;
                  ta.focus();
                  for (var i = 0; i < 3; i++) {
                    var type = i === 0 ? 'keydown' : (i === 1 ? 'keypress' : 'keyup');
                    ta.dispatchEvent(new KeyboardEvent(type, {
                      key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
                      bubbles: true, cancelable: true, isComposing: false
                    }));
                  }
                  return true;
                })()
                """, 10000, ct);
            await Task.Delay(700, ct);

            var remaining = await EvaluateAsync("(document.querySelector('textarea')||{}).value || ''", 5000, ct);
            if (!string.IsNullOrEmpty(remaining))
                await ClickSendButtonAsync(ct);
        }
        else
        {
            await ClickSendButtonAsync(ct);
        }

        var reply = await PollReplyAsync(before, timeoutMs, ct);

        try
        {
            await CallAsync("Page.bringToFront", null, 5000, ct);
            await CallAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true }, 5000, ct);
        }
        catch { }

        return reply;
    }

    private async Task ClickSendButtonAsync(CancellationToken ct)
    {
        await EvaluateAsync(KeySelectorJs + """
            (function(){
              var btns = pick(Sel.send);
              var btn = null, w = window.innerWidth, h = window.innerHeight;
              for (var i = 0; i < btns.length; i++) {
                var el = btns[i];
                if (el.offsetParent === null) continue;
                if (el.getAttribute('aria-disabled') === 'true') continue;
                var r = el.getBoundingClientRect();
                if (r.x < w * 0.5 || r.y < h * 0.6) continue;
                btn = el;
              }
              if (!btn) return false;
              btn.click();
              return true;
            })()
            """, 10000, ct);
    }

    private async Task UploadFilesAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        await CallAsync("DOM.enable", null, 10000, ct);
        var objectId = await EvaluateObjectIdAsync("document.querySelector('input[type=\"file\"]')", ct);
        if (string.IsNullOrEmpty(objectId))
            throw new CdpException("未找到 DeepSeek 文件上传控件。");

        var arr = new JsonArray();
        foreach (var f in files) arr.Add(f);
        await CallAsync("DOM.setFileInputFiles", new JsonObject
        {
            ["files"] = arr,
            ["objectId"] = objectId
        }, 20000, ct);

        await Task.Delay(3000, ct);
    }

    private sealed class ReplySnapshot
    {
        public int Count { get; set; }
        public string Text { get; set; } = "";
    }

    private async Task<ReplySnapshot> ReadReplySnapshotAsync(CancellationToken ct)
    {
        try
        {
            var raw = await EvaluateAsync(LastAssistantJs, 8000, ct);
            return JsonSerializer.Deserialize<ReplySnapshot>(raw, JsonOptions) ?? new ReplySnapshot();
        }
        catch
        {
            return new ReplySnapshot();
        }
    }

    private async Task<string> PollReplyAsync(ReplySnapshot before, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var sawNew = false;
        var stable = 0;
        var nullTicks = 0;
        var last = before.Text;
        var lastCount = before.Count;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(300, ct);
            var snap = await ReadReplySnapshotAsync(ct);

            if (snap.Count == 0)
            {
                nullTicks++;
                if (nullTicks >= 10)
                    throw new CdpException("连续多次未读取到 DeepSeek 回复，页面结构可能已变化。");
                continue;
            }
            nullTicks = 0;

            if (snap.Count > lastCount || (!string.IsNullOrEmpty(snap.Text) && snap.Text != before.Text))
                sawNew = true;

            if (sawNew && snap.Text == last && !string.IsNullOrEmpty(snap.Text))
                stable++;
            else
                stable = 0;

            last = snap.Text;
            lastCount = snap.Count;

            if (sawNew && stable >= 7)
                return last;
        }

        throw new CdpException("等待 DeepSeek 回复超时。");
    }

    public async Task<List<ConversationItem>> GetConversationsAsync(CancellationToken ct = default)
    {
        var js = """
            (function(){
              var out = [], seen = {};
              var links = document.querySelectorAll('a[href*="/a/chat/s/"]');
              for (var i = 0; i < links.length; i++) {
                var a = links[i];
                var href = a.href || '';
                if (!href || seen[href]) continue;
                var title = (a.innerText || a.textContent || '').replace(/\s+/g, ' ').trim();
                if (!title) continue;
                seen[href] = 1;
                out.push({ title: title, url: href });
              }
              return JSON.stringify(out);
            })()
            """;
        var raw = await EvaluateAsync(js, 15000, ct);
        if (string.IsNullOrWhiteSpace(raw)) return new List<ConversationItem>();
        return JsonSerializer.Deserialize<List<ConversationItem>>(raw, JsonOptions) ?? new List<ConversationItem>();
    }

    public static async Task<bool> CloseBrowserAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsEl)) return false;
            var wsUrl = wsEl.GetString();
            if (string.IsNullOrWhiteSpace(wsUrl)) return false;

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);
            var payload = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Browser.close\"}");
            await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct);
            await Task.Delay(500, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DisposeSocketAsync(bool clearPending)
    {
        _disposing = true;
        try { _cts.Cancel(); } catch { }
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { }

        if (_receiveTask != null)
        {
            try { await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _receiveTask = null;
        }

        if (clearPending) FailPending(new CdpException("CDP 连接已重置"));

        _cts.Dispose();
        _cts = new CancellationTokenSource();
        try { _ws.Dispose(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSocketAsync(clearPending: true);
        _sendLock.Dispose();
    }
}
