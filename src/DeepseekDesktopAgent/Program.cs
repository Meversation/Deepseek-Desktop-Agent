namespace DeepseekDesktopAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            var ok = SelfTest.Run();
            Environment.ExitCode = ok ? 0 : 1;
            return;
        }

        if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = Diagnostics.Run();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash(e.ExceptionObject as Exception);

        using var mutex = new Mutex(true, "Deepseek-Desktop-Agent.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("Deepseek-Desktop-Agent 已经在运行。", "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cfg = ConfigService.Load();
        if (!cfg.TermsAccepted)
        {
            using var dlg = new Form
            {
                Text = "使用条款与风险提示",
                Width = 600,
                Height = 400,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = ThemeManager.Current.Background,
                ForeColor = ThemeManager.Current.Text
            };

            var tb = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = ThemeManager.Current.Input,
                ForeColor = ThemeManager.Current.Text,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Text = "欢迎使用 Deepseek-Desktop-Agent！\r\n\r\n" +
                       "本程序通过 CDP 协议驱动 DeepSeek 网页版。请注意，自动化操作网页版可能违反 DeepSeek 的服务条款，并触发风控系统。\r\n\r\n" +
                       "一旦触发验证码，程序将自动停机并锁定输入，以保护您的账号。\r\n\r\n" +
                       "如果您继续使用本程序，代表您已了解并自愿承担账号被封禁的风险。请勿使用主力账号测试，并保持合理的操作间隔（如配置防封控延时）。\r\n\r\n" +
                       "点击“同意并继续”即表示接受上述条款。"
            };
            tb.Select(0, 0);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 50,
                Padding = new Padding(10)
            };
            var btnAgree = new Button { Text = "同意并继续", DialogResult = DialogResult.OK, Width = 100, Height = 32, FlatStyle = FlatStyle.Flat };
            var btnExit = new Button { Text = "拒绝并退出", DialogResult = DialogResult.Cancel, Width = 100, Height = 32, FlatStyle = FlatStyle.Flat };
            flow.Controls.Add(btnExit);
            flow.Controls.Add(btnAgree);

            dlg.Controls.Add(tb);
            dlg.Controls.Add(flow);
            dlg.AcceptButton = btnAgree;
            dlg.CancelButton = btnExit;

            if (dlg.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            cfg.TermsAccepted = true;
            ConfigService.Save(cfg);
        }

        Application.Run(new MainForm());
    }

    private static void ShowCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "Deepseek-Desktop-Agent_crash.log");
            File.AppendAllText(path, DateTime.Now + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch { }
        MessageBox.Show("Deepseek-Desktop-Agent 发生未处理异常：\n\n" + ex.Message, "Deepseek-Desktop-Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}