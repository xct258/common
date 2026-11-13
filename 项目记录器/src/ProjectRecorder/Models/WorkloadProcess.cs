using System;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 工作量工序：归属某个工作量项目独立，用于快捷记录完成次数。
/// </summary>
[DataContract]
public class WorkloadProcess
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    [DataMember] public string ProcessName { get; set; } = string.Empty;
    [DataMember] public int Order { get; set; }
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;
}
