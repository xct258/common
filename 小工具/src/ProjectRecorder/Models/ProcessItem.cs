using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 步骤配图（单张）：二进制随数据文件一起加密存储。
/// </summary>
[DataContract]
public class StepImage
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public string Name { get; set; } = string.Empty;
    /// <summary>图片字节：仅内存使用（新插入待保存/查看时暂存），不写入 processes.dat；
    /// 保存时落到 Data/images/&lt;id&gt;.img，读取时按需解密。</summary>
    [IgnoreDataMember] public byte[]? Data { get; set; }
}

/// <summary>
/// 工序操作步骤（逐条）：步骤内容 + 多张配图（配图独立加密存 Data/images/）。
/// 顺序 = 在列表中的位置。
/// </summary>
[DataContract]
public class FlowStep
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public int Order { get; set; }
    [DataMember] public string Text { get; set; } = string.Empty;
    [DataMember] public List<StepImage> Images { get; set; } = new();
}

/// <summary>
/// 工序：归属某个项目独立（各项目工序互不干扰），含逐条操作流程。
/// </summary>
[DataContract]
public class ProcessItem
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    [DataMember] public string ProcessName { get; set; } = string.Empty;
    [DataMember] public DateTime UpdatedTime { get; set; } = DateTime.Now;
    [DataMember] public List<FlowStep> Steps { get; set; } = new();
}
