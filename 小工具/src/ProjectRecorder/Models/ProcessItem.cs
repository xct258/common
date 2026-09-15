using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 工序操作步骤（逐条）：步骤内容 + 可选配图（二进制随数据文件一起加密存储）。
/// 顺序 = 在列表中的位置。
/// </summary>
[DataContract]
public class FlowStep
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public int Order { get; set; }
    [DataMember] public string Text { get; set; } = string.Empty;
    [DataMember] public byte[]? ImageData { get; set; }
    [DataMember] public string ImageName { get; set; } = string.Empty;
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
