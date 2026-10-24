using System.Diagnostics;
using System.Net.Http;

namespace DeepseekDesktopAgent;

public static class UpdateChecker
{
    public const string CurrentVersion = "2026年10月24日";
    private const string UpdateUrl = "https://meversation.github.io/Deepseek-Desktop-Agent/auto_update.txt";
    public const string ReleasesUrl = "https://github.com/Meversation/Deepseek-Desktop-Agent/releases";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<string?> FetchLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await Http.GetStringAsync(UpdateUrl, ct);
            var start = raw.IndexOf('[');
            if (start < 0) return null;
            var end = raw.IndexOf(']', start + 1);
            if (end <= start) return null;
            return raw.Substring(start + 1, end - start - 1).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }); }
        catch { }
    }
}
