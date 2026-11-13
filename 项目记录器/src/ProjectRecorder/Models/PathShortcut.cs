using System;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 快捷路径：文件夹路径或网址（名称 + 目标），点击用资源管理器 / 默认浏览器打开。
/// 与其它数据一样加密存储在 exe 同级 Data/shortcuts.dat。
/// </summary>
[DataContract]
public class PathShortcut
{
    public const string KindFolder = "Folder";
    public const string KindUrl = "Url";

    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>归属：项目名；通用模块的快捷路径为"通用"（旧数据可能为空，按通用处理）。</summary>
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    /// <summary>类型：Folder（文件夹，默认）或 Url（网址）；旧数据没有该字段，按文件夹处理。</summary>
    [DataMember] public string Kind { get; set; } = KindFolder;
    [DataMember] public string Name { get; set; } = string.Empty;
    [DataMember] public string FolderPath { get; set; } = string.Empty;
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;

    [IgnoreDataMember] public bool IsUrl => string.Equals(Kind, KindUrl, StringComparison.OrdinalIgnoreCase);
    [IgnoreDataMember] public string KindText => IsUrl ? "网址" : "文件夹";
}
