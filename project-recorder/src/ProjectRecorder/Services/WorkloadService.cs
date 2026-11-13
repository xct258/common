using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 工作量卡片视图模型：本班数量 + 最后一次执行时间文案。
/// 原先内嵌在 MainWindow 中，抽到服务层便于统计逻辑独立测试。
/// </summary>
public sealed class WorkCard
{
    public WorkloadProcess Process { get; set; } = null!;

    /// <summary>本班次数量（换班自动从 0 重新统计）。</summary>
    public int ShiftQty { get; set; }

    /// <summary>本班最后一次执行时间（HH:mm:ss，补录时为“补录”），无记录时为“暂无”。</summary>
    public string LastTimeText { get; set; } = "暂无";

    /// <summary>是否为「通用」页末尾固定的“其它”卡片。</summary>
    public bool IsOther { get; set; }
}

/// <summary>
/// 工作量纯逻辑：卡片统计、数量步进、负数冲减钳制。
/// 不依赖 WPF / 文件系统，可直接单元测试。
/// </summary>
public static class WorkloadService
{
    /// <summary>
    /// 按工序顺序生成本班卡片：只统计班次日期与白/夜班都一致的记录；
    /// 同类记录求和后最低为 0；补录记录的最后时间显示“补录”。
    /// </summary>
    public static List<WorkCard> ComputeCards(
        IEnumerable<WorkloadProcess> processes,
        IEnumerable<WorkloadRecord> records,
        DateTime shiftDate,
        string shiftName)
    {
        var recordList = records as IList<WorkloadRecord> ?? records.ToList();

        return processes
            .OrderBy(x => x.Order)
            .ThenBy(x => x.CreatedTime)
            .Select(p =>
            {
                var mine = recordList
                    .Where(r => r.ProcessName == p.ProcessName
                                && r.WorkDate.Date == shiftDate
                                && r.Shift == shiftName)
                    .ToList();
                var last = mine.OrderByDescending(r => r.CreatedTime).FirstOrDefault();
                // 补录记录没有具体时间：显示“补录”，不显示占位时间
                string lastText = last == null
                    ? "暂无"
                    : last.IsBackfill ? "补录" : last.CreatedTime.ToString("HH:mm:ss");
                return new WorkCard
                {
                    Process = p,
                    ShiftQty = Math.Max(0, mine.Sum(r => r.Quantity)),
                    LastTimeText = lastText
                };
            })
            .ToList();
    }

    /// <summary>
    /// 步进不经过 0：1 减一 = -1，-1 加一 = 1；结果钳制在 ±999999。
    /// </summary>
    public static int StepQuantity(int current, int delta)
    {
        int v = current + delta;
        if (v == 0) v = delta > 0 ? 1 : -1;
        if (v > 999999) v = 999999;
        if (v < -999999) v = -999999;
        return v;
    }

    /// <summary>
    /// 负数冲减：本班数量最低为 0，超出本班数量时直接按本班数量冲减到 0。
    /// 返回 0 表示无需记录（调用方应直接返回）。
    /// </summary>
    public static int ClampDeduction(int shiftQty, int qty)
    {
        if (qty < 0 && shiftQty + qty < 0)
            return -shiftQty;
        return qty;
    }
}
