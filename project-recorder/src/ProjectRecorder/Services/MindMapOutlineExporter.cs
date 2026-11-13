using System.Collections.Generic;
using System.IO;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 思维导图 YAML 导出：树形结构输出 title / level（深度，根为 0）/ path（层级路径编号，
/// 如 1-2-1）/ content / children；层级、路径、内容字段始终保留，方便给 AI 或程序当配置读取。
/// </summary>
public static class MindMapOutlineExporter
{
    /// <summary>导出为 UTF-8（带 BOM）的 YAML 文件，返回节点总数。</summary>
    public static int ExportYaml(string path, MindMap map)
    {
        var sb = new StringBuilder();
        sb.Append("title: ").AppendLine(YamlString(map.Name));
        sb.AppendLine("root:");
        AppendRoot(sb, map.Root, 0, "1");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return CountNodes(map.Root);
    }

    private static void AppendRoot(StringBuilder sb, MindNode? node, int level, string nodePath)
    {
        if (node == null) return;
        sb.Append("  title: ").AppendLine(YamlString(DisplayTitle(node)));
        sb.Append("  level: ").Append(level).AppendLine();
        sb.Append("  path: ").AppendLine(YamlString(nodePath));
        sb.Append("  content: ").AppendLine(YamlString(TrimContent(node)));

        var children = node.Children ?? new List<MindNode>();
        if (children.Count > 0)
        {
            sb.AppendLine("  children:");
            for (int i = 0; i < children.Count; i++)
                AppendItem(sb, children[i], 4, level + 1, nodePath + "-" + (i + 1));
        }
    }

    private static void AppendItem(StringBuilder sb, MindNode node, int indent, int level, string nodePath)
    {
        string pad = new string(' ', indent);
        string fieldPad = new string(' ', indent + 2);

        sb.Append(pad).Append("- title: ").AppendLine(YamlString(DisplayTitle(node)));
        sb.Append(fieldPad).Append("level: ").Append(level).AppendLine();
        sb.Append(fieldPad).Append("path: ").AppendLine(YamlString(nodePath));
        sb.Append(fieldPad).Append("content: ").AppendLine(YamlString(TrimContent(node)));

        var children = node.Children ?? new List<MindNode>();
        if (children.Count > 0)
        {
            sb.Append(fieldPad).AppendLine("children:");
            for (int i = 0; i < children.Count; i++)
                AppendItem(sb, children[i], indent + 4, level + 1, nodePath + "-" + (i + 1));
        }
    }

    private static string TrimContent(MindNode node)
        => (node.Content ?? string.Empty).Trim();

    private static string DisplayTitle(MindNode node)
    {
        string title = (node.Title ?? string.Empty).Trim();
        return title.Length == 0 ? "未命名" : title;
    }

    /// <summary>输出合法的 YAML 双引号字符串（转义 \ " 换行 制表符等）。</summary>
    private static string YamlString(string? text)
    {
        string t = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(t.Length + 2);
        sb.Append('"');
        foreach (char c in t)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\x").Append(((int)c).ToString("x2"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static int CountNodes(MindNode? node)
    {
        if (node == null) return 0;
        int count = 1;
        foreach (var child in node.Children ?? new List<MindNode>())
            count += CountNodes(child);
        return count;
    }
}
