using System.Collections.Generic;
using System.IO;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 思维导图文本大纲导出：Markdown 风格缩进列表，标题用「- 」逐层缩进，
/// 节点内容跟随在标题下方（同一缩进、无「-」前缀），方便直接粘贴给 AI 当需求描述。
/// </summary>
public static class MindMapOutlineExporter
{
    /// <summary>导出为 UTF-8（带 BOM）文本，返回节点总数。</summary>
    public static int ExportTxt(string path, MindMap map)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(NormalizeLine(map.Name));
        sb.AppendLine();
        AppendNode(sb, map.Root, 0);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return CountNodes(map.Root);
    }

    private static void AppendNode(StringBuilder sb, MindNode? node, int depth)
    {
        if (node == null) return;

        string indent = new string(' ', depth * 2);
        string title = (node.Title ?? string.Empty).Trim();
        sb.Append(indent).Append("- ").AppendLine(title.Length == 0 ? "未命名" : title);

        string content = (node.Content ?? string.Empty).Trim();
        if (content.Length > 0)
        {
            string bodyIndent = indent + "  ";
            foreach (string line in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                sb.Append(bodyIndent).AppendLine(line);
        }

        foreach (var child in node.Children ?? new List<MindNode>())
            AppendNode(sb, child, depth + 1);
    }

    private static int CountNodes(MindNode? node)
    {
        if (node == null) return 0;
        int count = 1;
        foreach (var child in node.Children ?? new List<MindNode>())
            count += CountNodes(child);
        return count;
    }

    private static string NormalizeLine(string? text)
        => (text ?? string.Empty).Trim().Replace('\r', ' ').Replace('\n', ' ');
}
