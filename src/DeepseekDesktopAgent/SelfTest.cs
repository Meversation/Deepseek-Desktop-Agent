using System.Text;

namespace DeepseekDesktopAgent;

public static class SelfTest
{
    public static bool Run()
    {
        var lines = new List<string>();
        var ok = true;

        void Check(string name, bool condition, string detail = "")
        {
            if (!condition) ok = false;
            lines.Add($"[{(condition ? "PASS" : "FAIL")}] {name}" + (detail.Length > 0 ? " - " + detail : ""));
        }

        try
        {
            var prompt = Prompt.Build(@"C:\TestWork", @"C:\Temp\Deepseek-Desktop-Agent");
            Check("提示词加载", prompt.Contains("DeepseekDesktopAgent_ps_识别头"), $"长度 {prompt.Length}");
            Check("提示词占位符替换", !prompt.Contains("{{WORKDIR}}") && !prompt.Contains("{{TEMP_WORKDIR}}"));

            var psReply = "前言\n# DeepseekDesktopAgent_ps_识别头_完全保留本行注释\nWrite-Output \"OK\"\n# DeepseekDesktopAgent_ps_识别尾_完全保留本行注释\n结尾";
            var ps = ToolProtocol.Parse(psReply);
            Check("工具解析 PowerShell", ps?.Kind == ToolKind.PowerShell && ps.Payload.Contains("Write-Output \"OK\""));

            var jpgReply = "# DeepseekDesktopAgent_getjpg_识别头_完全保留本行注释\nagent_getjpg_1_star(屏幕)_1_getjpg_end\n# DeepseekDesktopAgent_getjpg_识别尾_完全保留本行注释";
            var jpg = ToolProtocol.Parse(jpgReply);
            Check("工具解析 全屏截图", jpg?.Kind == ToolKind.Screenshot && jpg.FullScreen);

            var jpgWinReply = "# DeepseekDesktopAgent_getjpg_识别头_完全保留本行注释\nagent_getjpg_1_star(1234)_1_getjpg_end\n# DeepseekDesktopAgent_getjpg_识别尾_完全保留本行注释";
            var jpgWin = ToolProtocol.Parse(jpgWinReply);
            Check("工具解析 窗口截图", jpgWin?.Kind == ToolKind.Screenshot && jpgWin.WindowHandle.ToInt64() == 1234);

            var fileReply = "# DeepseekDesktopAgent_getfile_识别头_完全保留本行注释\nagent_getfile_head('D:\\1.jpg','C:\\_Project\\2.doc')agent_getfile_end\n# DeepseekDesktopAgent_getfile_识别尾_完全保留本行注释";
            var files = ToolProtocol.Parse(fileReply);
            Check("工具解析 上传文件", files?.Kind == ToolKind.UploadFile && files.Paths.Count == 2, string.Join(" | ", files?.Paths ?? new List<string>()));

            var mouseReply = "# DeepseekDesktopAgent_mouse_simulate_识别头_完全保留本行注释\nagent_mouse_simulate_1_start(200)_agent_mouse_simulate_1_end\nagent_mouse_simulate_2_start(300)_agent_mouse_simulate_2_end\nagent_mouse_simulate_3_start(1)_agent_mouse_simulate_3_end\n# DeepseekDesktopAgent_mouse_simulate_识别尾_完全保留本行注释";
            var mouse = ToolProtocol.Parse(mouseReply);
            Check("工具解析 鼠标", mouse?.Kind == ToolKind.Mouse && mouse.Numbers.SequenceEqual(new[] { 200, 300, 1 }));

            var kbReply = "# DeepseekDesktopAgent_keyboard_识别头_完全保留本行注释\nagent_keyboard_simulate_1_start(91)_agent_keyboard_simulate_1_end\nagent_keyboard_simulate_2_start(82)_agent_keyboard_simulate_2_end\n# DeepseekDesktopAgent_keyboard_识别尾_完全保留本行注释";
            var kb = ToolProtocol.Parse(kbReply);
            Check("工具解析 键盘", kb?.Kind == ToolKind.Keyboard && kb.Numbers.Take(2).SequenceEqual(new[] { 91, 82 }));

            var httpReply = "# DeepseekDesktopAgent_httpgetfile_识别头_完全保留本行注释\nhttpgetfile_1_star(https://www.bilibili.com/)_httpgetfile_1_end\n# DeepseekDesktopAgent_httpgetfile_识别尾_完全保留本行注释";
            var http = ToolProtocol.Parse(httpReply);
            Check("工具解析 HTTP", http?.Kind == ToolKind.HttpGetFile && http.Url == "https://www.bilibili.com/");

            Check("无工具回复不误判", ToolProtocol.Parse("普通回复，没有工具。") == null);

            var temp = AppPaths.CreateSessionTempDir();
            var systemFile = Path.Combine(temp, "system.txt");
            var text = "中文测试\n第二行";
            File.WriteAllText(systemFile, text, new UTF8Encoding(true));
            var bytes = File.ReadAllBytes(systemFile);
            Check("system.txt 为 UTF-8 BOM", bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Check("system.txt 中文回读", File.ReadAllText(systemFile, Encoding.UTF8).Contains("中文测试"));

            Prompt.EnsureSkills(temp);
            Check("skill_all.zip 释放", File.Exists(Path.Combine(temp, "skill_all.zip")));

            using var runner = new PowerShellRunner(temp);
            var result = runner.RunAsync("Write-Output \"中文OK\"", temp, CancellationToken.None, 60000)
                .GetAwaiter().GetResult();
            Check("PowerShell 后台执行与中文输出", result.Output.Contains("中文OK"), result.Output.Replace("\r", "").Replace("\n", " / "));
            Check("PowerShell 输出文件存在", File.Exists(result.OutFile));

            try { Directory.Delete(temp, true); } catch { }
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("[FAIL] 自测异常: " + ex);
        }

        var report = string.Join(Environment.NewLine, lines) + Environment.NewLine +
                     (ok ? "RESULT: PASS" : "RESULT: FAIL") + Environment.NewLine;
        var path = Path.Combine(Path.GetTempPath(), "deepseek_desktop_agent_selftest.txt");
        try { File.WriteAllText(path, report, new UTF8Encoding(true)); } catch { }
        return ok;
    }
}
