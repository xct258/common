using System;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 工作量记录：某项目/通用在某日期某班次的完成数量（+备注）。
/// 班次规则：8:30（含）~21:00（不含）为白班，其余为夜班。
/// </summary>
[DataContract]
public class WorkloadRecord
{
    public const string DayShift = "白班";
    public const string NightShift = "夜班";

    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    [DataMember] public string ProcessName { get; set; } = string.Empty;
    [DataMember] public DateTime WorkDate { get; set; } = DateTime.Today;
    [DataMember] public string Shift { get; set; } = string.Empty;
    [DataMember] public int Quantity { get; set; }
    [DataMember] public string Notes { get; set; } = string.Empty;
    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;

    public static string GetShiftName(DateTime time)
    {
        var t = time.TimeOfDay;
        if (t >= new TimeSpan(8, 30, 0) && t < new TimeSpan(21, 0, 0))
            return DayShift;
        return NightShift;
    }

    /// <summary>
    /// 班次日期：8:30（含）之后归当天；0:00~8:30 的夜班归前一日。
    /// 例：12 号 7 点 → 11 号夜班。
    /// </summary>
    public static DateTime GetShiftDate(DateTime time)
    {
        if (time.TimeOfDay < new TimeSpan(8, 30, 0))
            return time.Date.AddDays(-1);
        return time.Date;
    }
}
