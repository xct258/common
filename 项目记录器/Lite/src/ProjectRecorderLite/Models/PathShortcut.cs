using System;
using System.Runtime.Serialization;

namespace ProjectRecorderLite.Models;

/// <summary>
/// 快捷路径：文件夹路径或网址（名称 + 目标）。
/// Lite 版以明文 JSON 存储，不再加密。
/// </summary>
[DataContract]
public class PathShortcut
{
    public const string KindFolder = "Folder";
    public const string KindUrl = "Url";

    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>归属项目名。</summary>
    [DataMember] public string ProjectName { get; set; } = string.Empty;

    /// <summary>类型：Folder（文件夹，默认）或 Url（网址）。</summary>
    [DataMember] public string Kind { get; set; } = KindFolder;

    [DataMember] public string Name { get; set; } = string.Empty;

    /// <summary>文件夹路径或网址。</summary>
    [DataMember] public string FolderPath { get; set; } = string.Empty;

    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;

    [IgnoreDataMember] public bool IsUrl => string.Equals(Kind, KindUrl, StringComparison.OrdinalIgnoreCase);
    [IgnoreDataMember] public string KindText => IsUrl ? "网址" : "文件夹";
}
