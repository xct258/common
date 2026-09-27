using System;
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
    /// <summary>显示名（默认原文件名，可重命名）。</summary>
    [DataMember] public string Name { get; set; } = string.Empty;
    /// <summary>原文件名（导出解密副本时的默认文件名）。</summary>
    [DataMember] public string OriginalName { get; set; } = string.Empty;
    /// <summary>加密前的字节数。</summary>
    [DataMember] public long Size { get; set; }
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;

    [IgnoreDataMember]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名文件" : Name.Trim();

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
