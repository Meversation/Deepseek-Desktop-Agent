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
