using System.Text;
using System.Text.RegularExpressions;

namespace DeepseekDesktopAgent;

/// <summary>
/// 把 Markdown 文本转换成 RichTextBox 可用的 RTF。
/// 支持标题、粗体/斜体/删除线、行内代码、围栏代码块、无序/有序列表、引用、分隔线、链接和管道表格。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex HeadingRe = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UlRe = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OlRe = new(@"^(\s*)(\d{1,9})[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HrRe = new(@"^\s*([-*_])\s*(\1\s*){2,}$", RegexOptions.Compiled);
    private static readonly Regex TableSepRe = new(@"^\s*\|?[\s:|-]+\|[\s:|-]*$", RegexOptions.Compiled);

    public static string ToRtf(string markdown, ThemePalette p)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg936\deff0\uc1");
        sb.Append(@"{\fonttbl{\f0\fnil\fcharset134 Microsoft YaHei UI;}{\f1\fmodern\fcharset0 Consolas;}}");
        sb.Append(@"{\colortbl ;");
        foreach (var color in new[] { p.Text, p.SubtleText, p.Accent, p.Input, p.Text, p.Border })
            sb.Append($@"\red{color.R}\green{color.G}\blue{color.B};");
        sb.Append('}');
        sb.Append(@"\viewkind4\pard\f0\fs19\cf1");

        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[i++]);
                if (i < lines.Length) i++;
                AppendCodeBlock(sb, code);
                continue;
            }

            var heading = HeadingRe.Match(line);
            if (heading.Success)
            {
                AppendHeading(sb, heading.Groups[1].Length, heading.Groups[2].Value, p);
                i++;
                continue;
            }

            if (HrRe.IsMatch(line))
            {
                sb.Append(@"\pard\sa120\cf2\f0\fs16");
                sb.Append(Escape(new string('─', 32)));
                sb.Append(@"\par");
                i++;
                continue;
            }

            if (IsTableStart(lines, i))
            {
                i = AppendTable(sb, lines, i, p);
                continue;
            }

            var quote = line.TrimStart();
            if (quote.StartsWith('>'))
            {
                var text = quote[1..].TrimStart();
                sb.Append(@"\pard\sa80\li360\ri120\cf2\f0\fs19 ");
                sb.Append(Inline(text, p));
                sb.Append(@"\par");
                i++;
                continue;
            }

            var ul = UlRe.Match(line);
            if (ul.Success)
            {
                var indent = ul.Groups[1].Length / 2;
                sb.Append($@"\pard\sa60\li{420 + indent * 280}\fi-240\cf1\f0\fs19 ");
                sb.Append(Escape("•  "));
                sb.Append(Inline(ul.Groups[3].Value, p));
                sb.Append(@"\par");
                i++;
                continue;
            }

            var ol = OlRe.Match(line);
            if (ol.Success)
            {
                var indent = ol.Groups[1].Length / 2;
                sb.Append($@"\pard\sa60\li{420 + indent * 280}\fi-280\cf1\f0\fs19 ");
                sb.Append(ol.Groups[2].Value).Append(".  ");
                sb.Append(Inline(ol.Groups[3].Value, p));
                sb.Append(@"\par");
                i++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append(@"\pard\sa60\par");
                i++;
                continue;
            }

            sb.Append(@"\pard\sa90\cf1\f0\fs19 ");
            sb.Append(Inline(line, p));
            sb.Append(@"\par");
            i++;
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendHeading(StringBuilder sb, int level, string text, ThemePalette p)
    {
        var size = level switch { 1 => 34, 2 => 29, 3 => 25, 4 => 22, 5 => 21, _ => 20 };
        var color = level <= 3 ? 3 : 1;
        sb.Append($@"\pard\sb{180 - level * 20}\sa100\cf{color}\f0\fs{size}\b ");
        sb.Append(Inline(text, p));
        sb.Append(@"\b0\par");
    }

    private static void AppendCodeBlock(StringBuilder sb, List<string> code)
    {
        sb.Append(@"\pard\sa120\li120\ri120\f1\fs18\cf5\highlight4 ");
        if (code.Count == 0) sb.Append(' ');
        for (var i = 0; i < code.Count; i++)
        {
            if (i > 0) sb.Append(@"\par ");
            sb.Append(Escape(code[i]));
        }
        sb.Append(@"\highlight0\f0\fs19\cf1\par");
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (!lines[index].Contains('|')) return false;
        if (index + 1 >= lines.Length) return false;
        return TableSepRe.IsMatch(lines[index + 1]) && lines[index + 1].Contains('-');
    }

    private static int AppendTable(StringBuilder sb, string[] lines, int index, ThemePalette p)
    {
        var header = SplitRow(lines[index]);
        var rows = new List<string[]>();
        var i = index + 2;
        while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]))
            rows.Add(SplitRow(lines[i++]));

        sb.Append(@"\pard\sa60\f0\fs18 ");
        AppendRow(sb, header, p, bold: true);
        sb.Append(@"\pard\sa100\cf5\f0\fs14 ");
        sb.Append(Escape(new string('─', Math.Clamp(header.Length * 6, 12, 32))));
        sb.Append(@"\par");
        foreach (var row in rows)
        {
            sb.Append(@"\pard\sa40\f0\fs18 ");
            AppendRow(sb, row, p, bold: false);
        }
        return i;
    }

    private static void AppendRow(StringBuilder sb, string[] cells, ThemePalette p, bool bold)
    {
        if (bold) sb.Append(@"\b\cf1 ");
        if (cells.Length == 0) sb.Append(' ');
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(@"\tab ");
            sb.Append(Inline(cells[i], p));
        }
        if (bold) sb.Append(@"\b0\cf1 ");
        sb.Append(@"\par");
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
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    sb.Append(@"{\f1\highlight4 ");
                    sb.Append(Escape(text[(i + 1)..end]));
                    sb.Append('}');
                    i = end + 1;
                    continue;
                }
            }

            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    sb.Append(@"{\b ").Append(Inline(text[(i + 2)..end], p)).Append('}');
                    i = end + 2;
                    continue;
                }
            }

            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    sb.Append(@"{\strike ").Append(Inline(text[(i + 2)..end], p)).Append('}');
                    i = end + 2;
                    continue;
                }
            }

            if (c is '*' or '_')
            {
                var end = text.IndexOf(c, i + 1);
                if (end > i + 1)
                {
                    sb.Append(@"{\i ").Append(Inline(text[(i + 1)..end], p)).Append('}');
                    i = end + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var end = text.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        var label = text[(i + 1)..close];
                        var url = text[(close + 2)..end];
                        sb.Append(@"{\cf3\ul ").Append(Inline(label, p)).Append(@"}");
                        if (!url.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                            sb.Append(@"{\cf2\ulnone ").Append(Escape(" (" + url + ")")).Append('}');
                        i = end + 1;
                        continue;
                    }
                }
            }

            sb.Append(Escape(c.ToString()));
            i++;
        }
        return sb.ToString();
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\t': sb.Append(@"\tab "); break;
                case '\n': sb.Append(@"\par "); break;
                default:
                    if (ch < 0x20) break;
                    if (ch < 0x80) sb.Append(ch);
                    else sb.Append(@"\u").Append((short)ch).Append('?');
                    break;
            }
        }
        return sb.ToString();
    }
}
