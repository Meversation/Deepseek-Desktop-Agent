namespace EAgent;

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

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash(e.ExceptionObject as Exception);

        using var mutex = new Mutex(true, "EAgent.CSharp.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("EAgent 已经在运行。", "EAgent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void ShowCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "EAgent_crash.log");
            File.AppendAllText(path, DateTime.Now + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch { }
        MessageBox.Show("EAgent 发生未处理异常：\n\n" + ex.Message, "EAgent", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
