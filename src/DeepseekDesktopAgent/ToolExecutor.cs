using System.Drawing;
using System.Net.Http;

namespace DeepseekDesktopAgent;

public interface IToolHost
{
    string TempDir { get; }
    string WorkDir { get; }
    bool IsAutoExecute { get; }
    CancellationToken Token { get; }
    Task<bool> ConfirmPowerShellAsync(string code);
    void BeginTool(string name, string detail);
    void ToolOutput(string line);
    void EndTool(string name, string result, int? exitCode);
    void SetStatus(string text);
    Task PrepareCaptureAsync();
    Task FinishCaptureAsync();
}

public sealed class ToolExecutor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly PowerShellRunner _ps;
    private readonly IToolHost _host;

    public ToolExecutor(PowerShellRunner ps, IToolHost host)
    {
        _ps = ps;
        _host = host;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call)
    {
        switch (call.Kind)
        {
            case ToolKind.PowerShell:
                return await RunPowerShellAsync(call);
            case ToolKind.Screenshot:
                return await TakeScreenshotAsync(call);
            case ToolKind.UploadFile:
                return UploadFiles(call);
            case ToolKind.Mouse:
                return ControlMouse(call);
            case ToolKind.Keyboard:
                return ControlKeyboard(call);
            case ToolKind.HttpGetFile:
                return await HttpGetFileAsync(call);
            default:
                return new ToolResult { Message = "//[工具][错误]//\n未知工具\n//[END]//" };
        }
    }

    private async Task<ToolResult> RunPowerShellAsync(ToolCall call)
    {
        if (!_host.IsAutoExecute)
        {
            var allowed = await _host.ConfirmPowerShellAsync(call.Payload);
            if (!allowed)
            {
                _host.BeginTool("PowerShell", call.Payload);
                _host.EndTool("PowerShell", "用户拒绝执行", null);
                return new ToolResult
                {
                    Message = "//[工具][用户拒绝]//\n用户拒绝执行该命令。\n//[END]//",
                    Executed = false
                };
            }
        }

        _host.BeginTool("PowerShell", call.Payload);
        try
        {
            var result = await _ps.RunAsync(call.Payload, _host.WorkDir, _host.Token);
            _host.EndTool("PowerShell", result.Output, result.ExitCode);

            var output = result.Output;
            if (string.IsNullOrEmpty(output))
                output = result.ExitCode == 0 ? "(无输出)" : $"(无输出，退出码 {result.ExitCode})";

            if (output.Length > 1000)
            {
                return new ToolResult
                {
                    Message = "//[工具][取回结果]//\n" + result.OutFile + "\n//[END]//",
                    Files = new List<string> { result.OutFile }
                };
            }

            return new ToolResult
            {
                Message = "//[工具][取回结果]//\n" + output + "\n//[END]//"
            };
        }
        catch (OperationCanceledException)
        {
            _host.EndTool("PowerShell", "已取消", -1);
            throw;
        }
        catch (Exception ex)
        {
            _host.EndTool("PowerShell", ex.Message, -1);
            return new ToolResult
            {
                Message = "!![工具][错误报告]!!\nPowerShell 执行失败: " + ex.Message + "\n//[END]//"
            };
        }
    }

    private async Task<ToolResult> TakeScreenshotAsync(ToolCall call)
    {
        var target = call.FullScreen ? "全屏" : "窗口句柄=" + call.WindowHandle;
        _host.BeginTool("截图", target);
        _host.SetStatus("正在截图...");

        Bitmap? bmp = null;
        try
        {
            await _host.PrepareCaptureAsync();
            await Task.Delay(500, _host.Token);

            if (call.FullScreen)
            {
                var (_, _, width, height) = Native.GetPrimaryScreenRect();
                bmp = Native.CaptureScreenRegion(0, 0, width, height);
            }
            else if (Native.IsWindow(call.WindowHandle))
            {
                bmp = Native.CaptureWindow(call.WindowHandle);
            }
            else
            {
                var (_, _, width, height) = Native.GetPrimaryScreenRect();
                bmp = Native.CaptureScreenRegion(0, 0, width, height);
            }

            var file = Path.Combine(_host.TempDir, "屏幕截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".jpg");
            SaveJpeg(bmp, file, 85);

            var (physicalWidth, physicalHeight, bits, refresh) = Native.GetDisplayInfo();
            var scale = Native.GetPrimaryScale();
            var logicalWidth = (int)Math.Round(physicalWidth / scale);
            var logicalHeight = (int)Math.Round(physicalHeight / scale);
            var (mx, my) = Native.GetCursorLogical();

            var message = "//[工具][屏幕截图]//\n" +
                          $"【附加信息】屏幕色深={bits}，物理屏幕宽度={physicalWidth}->逻辑宽度{logicalWidth}，" +
                          $"物理屏幕高度={physicalHeight}->逻辑高度{logicalHeight}，" +
                          $"屏幕刷新率={refresh}当前鼠标所在的屏幕逻辑坐标XY为：({mx},{my})\n" +
                          "//[END]//";

            _host.EndTool("截图", file, null);
            return new ToolResult { Message = message, Files = new List<string> { file } };
        }
        catch (Exception ex)
        {
            _host.EndTool("截图", ex.Message, null);
            return new ToolResult { Message = "!![工具][错误]!!\n截图失败: " + ex.Message + "\n//[END]//" };
        }
        finally
        {
            bmp?.Dispose();
            await _host.FinishCaptureAsync();
        }
    }

    private static void SaveJpeg(Bitmap bmp, string path, int quality)
    {
        var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        if (encoder == null)
        {
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
            return;
        }
        using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
        parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        bmp.Save(path, encoder, parameters);
    }

    private ToolResult UploadFiles(ToolCall call)
    {
        _host.BeginTool("上传文件", string.Join(",", call.Paths));
        var existing = call.Paths.Where(File.Exists).ToList();
        if (existing.Count == 0)
        {
            _host.EndTool("上传文件", "文件不存在", null);
            return new ToolResult
            {
                Message = "!![工具][错误]!!\n没有找到可上传的文件: " + string.Join(",", call.Paths) + "\n//[END]//"
            };
        }

        var display = string.Join(",", existing.Select(p => "\"" + p + "\""));
        _host.EndTool("上传文件", display, null);
        return new ToolResult
        {
            Message = "//[工具][上传文件]//\n" + display + "\n//[END]//",
            Files = existing
        };
    }

    private ToolResult ControlMouse(ToolCall call)
    {
        if (call.Numbers.Count < 3)
        {
            return new ToolResult { Message = "!![工具][错误]!!\n鼠标参数不足，需要 X、Y、类型 三个整数。\n//[END]//" };
        }

        var x = call.Numbers[0];
        var y = call.Numbers[1];
        var mode = call.Numbers[2];
        _host.BeginTool("模拟鼠标", $"X={x}, Y={y}, 类型={mode}");
        try
        {
            Thread.Sleep(300);
            Native.LogicalToPhysical(x, y, out var px, out var py);
            Native.ClickMouse(px, py, mode);
            var (lx, ly) = Native.GetCursorLogical();
            _host.EndTool("模拟鼠标", $"已执行 ({lx},{ly})", null);
            return new ToolResult
            {
                Message = "//[工具][模拟控制鼠标]//\n已执行(除非用户移动鼠标)。当前鼠标所在的屏幕逻辑坐标XY为：(" + lx + "," + ly + ")\n//[END]//"
            };
        }
        catch (Exception ex)
        {
            _host.EndTool("模拟鼠标", ex.Message, null);
            return new ToolResult { Message = "!![工具][错误]!!\n鼠标操作失败: " + ex.Message + "\n//[END]//" };
        }
    }

    private ToolResult ControlKeyboard(ToolCall call)
    {
        var vks = call.Numbers.Take(3).Where(v => v > 0).ToArray();
        var hwnd = call.WindowHandle != IntPtr.Zero ? call.WindowHandle.ToInt64() : 0;
        _host.BeginTool("模拟键盘", "VK=" + string.Join(",", vks) + (hwnd > 0 ? " 句柄=" + hwnd : ""));
        try
        {
            if (hwnd > 0) Native.ForceFocus(new IntPtr(hwnd));
            Thread.Sleep(300);
            Native.PressKeys(vks);
            _host.EndTool("模拟键盘", "已执行", null);
            return new ToolResult
            {
                Message = "//[工具][模拟控制键盘]//\n已尝试在当前焦点窗口内执行\n//[END]//"
            };
        }
        catch (Exception ex)
        {
            _host.EndTool("模拟键盘", ex.Message, null);
            return new ToolResult { Message = "!![工具][错误]!!\n键盘操作失败: " + ex.Message + "\n//[END]//" };
        }
    }

    private async Task<ToolResult> HttpGetFileAsync(ToolCall call)
    {
        _host.BeginTool("HTTP读文件", call.Url);
        if (string.IsNullOrWhiteSpace(call.Url))
        {
            _host.EndTool("HTTP读文件", "URL 为空", null);
            return new ToolResult { Message = "!![工具][错误]!!\nURL 为空。\n//[END]//" };
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync(call.Url, _host.Token);
            var file = Path.Combine(_host.TempDir, "HTTP读文件_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".html");
            await File.WriteAllBytesAsync(file, bytes, _host.Token);
            _host.EndTool("HTTP读文件", file + $" ({bytes.Length} 字节)", null);
            return new ToolResult
            {
                Message = "//[工具][HTTP读文件]//\n\"" + file + "\"\n//[END]//",
                Files = new List<string> { file }
            };
        }
        catch (Exception ex)
        {
            _host.EndTool("HTTP读文件", ex.Message, null);
            return new ToolResult { Message = "!![工具][错误]!!\nHTTP 读取失败: " + ex.Message + "\n//[END]//" };
        }
    }
}
