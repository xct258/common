using System;
using System.IO;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 加密文件：文件内容单独 AES-256 加密存 Data/files/&lt;id&gt;.enc，
/// 这里只存元数据（本身加密存 Data/files.dat），绝不明文落盘。
/// </summary>
[DataContract]
public class EncryptedFile
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>所属项目分组（空视为「通用」）。</summary>
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    /// <summary>显示名（默认原文件名，可重命名）。</summary>
    [DataMember] public string Name { get; set; } = string.Empty;
    /// <summary>原文件名（导出解密副本时的默认文件名）。</summary>
    [DataMember] public string OriginalName { get; set; } = string.Empty;
    /// <summary>加密前的字节数。</summary>
    [DataMember] public long Size { get; set; }
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;
    /// <summary>内容最后更新时间（在线编辑保存时刷新）；老数据缺失时回填为导入时间。</summary>
    [DataMember] public DateTime UpdatedTime { get; set; } = DateTime.Now;

    /// <summary>列表显示的时间：有过更新显示更新时间，否则显示导入时间。</summary>
    [IgnoreDataMember]
    public DateTime DisplayTime => UpdatedTime > CreatedTime ? UpdatedTime : CreatedTime;

    /// <summary>列表显示的时间标签：“更新”/“导入”。</summary>
    [IgnoreDataMember]
    public string DisplayTimeLabel => UpdatedTime > CreatedTime ? "更新" : "导入";

    [IgnoreDataMember]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名文件" : Name.Trim();

    /// <summary>AvalonEdit 高亮定义名（MarkDown/Python/Bash）；不支持在线编辑的类型返回空串。</summary>
    [IgnoreDataMember]
    public string EditorLanguage => GetEditorLanguage(OriginalName);

    /// <summary>是否支持在新窗口中在线编辑（按原文件名的 .md/.sh/.py 后缀判断）。</summary>
    [IgnoreDataMember]
    public bool CanEdit => EditorLanguage.Length > 0;

    public static string GetEditorLanguage(string? fileName)
    {
        string ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        switch (ext)
        {
            case ".md":
            case ".markdown":
                return "MarkDown";
            case ".py":
                return "Python";
            case ".sh":
            case ".bash":
                return "Bash";
            default:
                return string.Empty;
        }
    }

    [IgnoreDataMember]
    public string SizeText => FormatSize(Size);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
