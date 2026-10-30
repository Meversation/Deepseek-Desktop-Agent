using System.Text;
using System.Text.RegularExpressions;

namespace DeepseekDesktopAgent;

/// <summary>
/// Markdown → RTF 渲染器。纯函数、不依赖 WinForms 控件、不使用第三方库。
/// 覆盖 ATX 标题、粗/斜/粗斜/删除线、行内代码、围栏代码块、有序/无序/任务列表、
/// 引用、分隔线、链接与裸 URL、管道表格。非 ASCII（含代理对）走 \u 转义，
/// \ { } 正确转义，连续空行压缩为一个，空/未闭合输入安全。
/// </summary>
public static class MarkdownRenderer
{
    private const int CfText = 1, CfSubtle = 2, CfAccent = 3, CfInput = 4, CfBorder = 6;

    private static readonly Regex HeadingRe = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UlRe = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OlRe = new(@"^(\s*)(\d{1,9})[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HrRe = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSepRe = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex TaskRe = new(@"^\[([ xX])\]\s+(.*)$", RegexOptions.Compiled);

    public static string ToRtf(string markdown, ThemePalette p)
    {
        var sb = new StringBuilder(4096);
        sb.Append(@"{\rtf1\ansi\deff0\uc1");
        sb.Append(@"{\fonttbl{\f0\fnil\fcharset0 Microsoft YaHei UI;}{\f1\fmodern\fcharset0 Consolas;}}");
        sb.Append(@"{\colortbl ;");
        foreach (var c in new[] { p.Text, p.SubtleText, p.Accent, p.Input, p.Text, p.Border })
            sb.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';');
        sb.Append('}');
        sb.Append(@"\viewkind4\pard\f0\fs19\cf").Append(CfText).Append(' ');

        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0; bool prevBlank = false, any = false;
        while (i < lines.Length)
        {
            var line = lines[i] ?? string.Empty;

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i++]);
                if (i < lines.Length) i++;
                AppendCodeBlock(sb, code); prevBlank = false; any = true; continue;
            }

            var h = HeadingRe.Match(line);
            if (h.Success) { AppendHeading(sb, h.Groups[1].Length, h.Groups[2].Value, p); prevBlank = false; any = true; i++; continue; }

            if (HrRe.IsMatch(line))
            {
                sb.Append(@"\pard\sa120\cf").Append(CfBorder).Append(@"\f0\fs16 ").Append(Escape(new string('─', 32)))
                  .Append(@"\cf").Append(CfText).Append(@"\par ");
                prevBlank = false; any = true; i++; continue;
            }

            if (IsTableStart(lines, i)) { i = AppendTable(sb, lines, i, p); prevBlank = false; any = true; continue; }

            var lt = line.TrimStart();
            if (lt.StartsWith('>'))
            {
                var text = lt.Length > 1 ? lt[1..].TrimStart() : string.Empty;
                sb.Append(@"\pard\sa60\li360\ri120\cf").Append(CfSubtle).Append(@"\f0\fs19 ")
                  .Append(Inline(text, p)).Append(@"\cf").Append(CfText).Append(@"\par ");
                prevBlank = false; any = true; i++; continue;
            }

            var ul = UlRe.Match(line);
            if (ul.Success) { AppendListItem(sb, ul.Groups[1].Length, ul.Groups[3].Value, p, false, 0); prevBlank = false; any = true; i++; continue; }

            var ol = OlRe.Match(line);
            if (ol.Success)
            {
                int num = int.TryParse(ol.Groups[2].Value, out var n) && n > 0 ? n : 1;
                AppendListItem(sb, ol.Groups[1].Length, ol.Groups[3].Value, p, true, num);
                prevBlank = false; any = true; i++; continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (!prevBlank && any) { sb.Append(@"\pard\sa0\par "); prevBlank = true; }
                i++; continue;
            }

            sb.Append(@"\pard\sa90\cf").Append(CfText).Append(@"\f0\fs19\b0\i0 ").Append(Inline(line, p)).Append(@"\par ");
            prevBlank = false; any = true; i++;
        }

        if (!any) sb.Append(@"\pard\sa0\par ");
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendHeading(StringBuilder sb, int level, string text, ThemePalette p)
    {
        int size = level switch { 1 => 34, 2 => 29, 3 => 25, 4 => 22, 5 => 21, _ => 20 };
        int color = level <= 3 ? CfAccent : CfText;
        sb.Append(@"\pard\sb180\sa80\cf").Append(color).Append(@"\f0\fs").Append(size).Append(@"\b ")
          .Append(Inline(text, p)).Append(@"\b0\cf").Append(CfText).Append(@"\par ");
    }

    private static void AppendCodeBlock(StringBuilder sb, List<string> code)
    {
        sb.Append(@"\pard\sa120\li120\ri120\f1\fs18\b0\i0\cf").Append(CfText).Append(@"\highlight").Append(CfInput).Append(' ');
        if (code.Count == 0) sb.Append(' ');
        else for (int i = 0; i < code.Count; i++) { if (i > 0) sb.Append(@"\line "); sb.Append(Escape(code[i])); }
        sb.Append(@"\highlight0\f0\fs19\cf").Append(CfText).Append(@"\par ");
    }

    private static void AppendListItem(StringBuilder sb, int leadingSpaces, string content, ThemePalette p, bool ordered, int orderNum)
    {
        int level = Math.Clamp(leadingSpaces / 2, 0, 5);
        int li = 420 + level * 280, fi = ordered ? -300 : -240;
        sb.Append(@"\pard\sa60\li").Append(li).Append(@"\fi").Append(fi).Append(@"\cf").Append(CfText).Append(@"\f0\fs19\b0\i0 ");
        var task = TaskRe.Match(content);
        if (task.Success)
        {
            sb.Append(Escape(task.Groups[1].Value is "x" or "X" ? "☑  " : "☐  "));
            sb.Append(Inline(task.Groups[2].Value, p));
        }
        else
        {
            sb.Append(ordered ? orderNum.ToString() + ".  " : Escape("•  "));
            sb.Append(Inline(content, p));
        }
        sb.Append(@"\par ");
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (!lines[index].Contains('|')) return false;
        if (index + 1 >= lines.Length) return false;
        var sep = lines[index + 1];
        return sep.Contains('-') && sep.Contains('|') && TableSepRe.IsMatch(sep);
    }

    private static int AppendTable(StringBuilder sb, string[] lines, int index, ThemePalette p)
    {
        var header = SplitRow(lines[index]);
        var rows = new List<string[]>();
        int i = index + 2;
        while (i < lines.Length) { var l = lines[i]; if (string.IsNullOrWhiteSpace(l) || !l.Contains('|')) break; rows.Add(SplitRow(l)); i++; }

        int cols = Math.Max(header.Length, 1);
        foreach (var r in rows) if (r.Length > cols) cols = r.Length;

        // 动态计算列宽：根据每列最大字符数加权分配总宽度（近似 8000 缇）
        const int TableWidth = 8000;
        var colLengths = new int[cols];
        for (int c = 0; c < cols; c++) colLengths[c] = GetDisplayWidth(c < header.Length ? header[c] : "");
        foreach (var r in rows)
            for (int c = 0; c < cols; c++)
            {
                var len = GetDisplayWidth(c < r.Length ? r[c] : "");
                if (len > colLengths[c]) colLengths[c] = len;
            }

        var totalLen = 0;
        foreach (var len in colLengths) totalLen += Math.Max(len, 4); // 最小保证 4 个字宽
        var cellXs = new int[cols];
        int currentX = 0;
        for (int c = 0; c < cols; c++)
        {
            var w = (int)Math.Round((double)Math.Max(colLengths[c], 4) / totalLen * TableWidth);
            currentX += w;
            cellXs[c] = currentX;
        }

        AppendTableRow(sb, header, cols, cellXs, p, true);
        foreach (var r in rows) AppendTableRow(sb, r, cols, cellXs, p, false);
        sb.Append(@"\pard\sa0\par ");
        return i;
    }

    private static int GetDisplayWidth(string s)
    {
        int w = 0;
        foreach (var c in s) w += (c > 0x2E80) ? 2 : 1;
        return w;
    }

    private static void AppendTableRow(StringBuilder sb, string[] cells, int cols, int[] cellXs, ThemePalette p, bool bold)
    {
        sb.Append(@"\trowd\trgaph108\trleft-108 ");
        for (int c = 0; c < cols; c++)
        {
            // 顶部、底部、左侧、右侧 四周边框
            sb.Append(@"\clbrdrt\brdrs\brdrw10\brdrcf").Append(CfBorder).Append(' ')
              .Append(@"\clbrdrb\brdrs\brdrw10\brdrcf").Append(CfBorder).Append(' ')
              .Append(@"\clbrdrl\brdrs\brdrw10\brdrcf").Append(CfBorder).Append(' ')
              .Append(@"\clbrdrr\brdrs\brdrw10\brdrcf").Append(CfBorder).Append(' ')
              .Append(@"\cellx").Append(cellXs[c]).Append(' ');
        }
        sb.Append(@"\pard\intbl\cf").Append(CfText).Append(@"\f0\fs18 ");
        if (bold) sb.Append(@"\b ");
        for (int c = 0; c < cols; c++) sb.Append(Inline(c < cells.Length ? cells[c] : string.Empty, p)).Append(@"\cell ");
        if (bold) sb.Append(@"\b0 ");
        sb.Append(@"\row ");
    }

    private static string[] SplitRow(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|')) text = text[..^1];
        return text.Split('|').Select(c => c.Trim()).ToArray();
    }

    private static string Inline(string text, ThemePalette p)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i) { sb.Append(@"{\f1\cf").Append(CfText).Append(@"\highlight").Append(CfInput).Append(' ').Append(Escape(text[(i + 1)..end])).Append('}'); i = end + 1; continue; }
            }
            if (c == '*' && i + 2 < text.Length && text[i + 1] == '*' && text[i + 2] == '*')
            {
                int end = text.IndexOf("***", i + 3, StringComparison.Ordinal);
                if (end > i + 3) { sb.Append(@"{\b\i ").Append(Inline(text[(i + 3)..end], p)).Append('}'); i = end + 3; continue; }
            }
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2) { sb.Append(@"{\b ").Append(Inline(text[(i + 2)..end], p)).Append('}'); i = end + 2; continue; }
            }
            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
            {
                int end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 2) { sb.Append(@"{\strike ").Append(Inline(text[(i + 2)..end], p)).Append('}'); i = end + 2; continue; }
            }
            if (c == '*' || c == '_')
            {
                int end = text.IndexOf(c, i + 1);
                if (end > i + 1) { sb.Append(@"{\i ").Append(Inline(text[(i + 1)..end], p)).Append('}'); i = end + 1; continue; }
            }
            if (c == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    int end = text.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        var label = text[(i + 1)..close]; var url = text[(close + 2)..end];
                        sb.Append(@"{\cf").Append(CfAccent).Append(@"\ul ").Append(Inline(label, p)).Append('}');
                        if (!string.Equals(url, label, StringComparison.OrdinalIgnoreCase))
                            sb.Append(@"{\cf").Append(CfSubtle).Append(' ').Append(Escape("(" + url + ")")).Append('}');
                        i = end + 1; continue;
                    }
                }
            }
            if (c == 'h')
            {
                var span = text.AsSpan(i);
                bool isHttps = span.StartsWith("https://", StringComparison.Ordinal);
                bool isHttp = !isHttps && span.StartsWith("http://", StringComparison.Ordinal);
                if (isHttps || isHttp)
                {
                    int j = i + (isHttps ? 8 : 7);
                    while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != ')' && text[j] != ']' && text[j] != '>' && text[j] != '"') j++;
                    while (j > i && (text[j - 1] == '.' || text[j - 1] == ',' || text[j - 1] == ';' || text[j - 1] == ':')) j--;
                    sb.Append(@"{\cf").Append(CfAccent).Append(@"\ul ").Append(Escape(text[i..j])).Append('}');
                    i = j; continue;
                }
            }
            sb.Append(Escape(c.ToString()));
            i++;
        }
        return sb.ToString();
    }

    /// <summary>转义为 RTF 内容。非 ASCII 走 \uNNNN?；代理对拆成两个 \u，避免 emoji 乱码。</summary>
    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); continue;
                case '{': sb.Append(@"\{"); continue;
                case '}': sb.Append(@"\}"); continue;
                case '\t': sb.Append(@"\tab "); continue;
                case '\r': continue;
                case '\n': sb.Append(@"\line "); continue;
            }
            if (ch < 0x20) continue;
            if (ch < 0x80) { sb.Append(ch); continue; }
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(@"\u").Append(unchecked((short)ch)).Append('?');
                sb.Append(@"\u").Append(unchecked((short)text[i + 1])).Append('?');
                i++; continue;
            }
            sb.Append(@"\u").Append(unchecked((short)ch)).Append('?');
        }
        return sb.ToString();
    }
}
