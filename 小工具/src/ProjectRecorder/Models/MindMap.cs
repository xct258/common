using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 思维导图节点（树形）：标题（画布显示）+ 内容（弹窗编辑），子节点顺序 = 绘制顺序。
/// </summary>
[DataContract]
public class MindNode
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>标题：画布上只显示标题。</summary>
    [DataMember] public string Title { get; set; } = string.Empty;
    /// <summary>内容：双击节点在弹窗里编辑，画布上不显示。</summary>
    [DataMember] public string Content { get; set; } = string.Empty;
    [DataMember] public List<MindNode> Children { get; set; } = new List<MindNode>();

    /// <summary>旧版纯文本字段（Text）：仅用于把老数据迁移到 Title，迁移后不再写入。</summary>
    [DataMember(Name = "Text", EmitDefaultValue = false)]
    public string? LegacyText { get; set; }
}

/// <summary>
/// 思维导图：通用模块独立功能，一张图一棵节点树，加密存 Data/mindmaps.dat。
/// </summary>
[DataContract]
public class MindMap
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public string Name { get; set; } = string.Empty;
    /// <summary>所属项目分组（空视为「通用」）。</summary>
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    [DataMember] public MindNode Root { get; set; } = new MindNode();
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;
    [DataMember] public DateTime UpdatedTime { get; set; } = DateTime.Now;

    [IgnoreDataMember]
    public int NodeCount => CountNodes(Root);

    private static int CountNodes(MindNode? node)
    {
        if (node == null) return 0;
        int count = 1;
        if (node.Children != null)
        {
            foreach (var child in node.Children)
                count += CountNodes(child);
        }
        return count;
    }
}
