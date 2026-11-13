using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>汇总行：项目 + 工序 + 数量。</summary>
public sealed class WorkloadSummaryRow
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>每日汇总行：日期 + 班次 + 项目 + 工序 + 数量。</summary>
public sealed class WorkloadDailyRow
{
    public DateTime WorkDate { get; set; }
    public string Shift { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>
/// 工作量导出排序/聚合（纯逻辑）：
/// 项目按界面左侧顺序；项目内工序按工作量页卡片顺序（Order → CreatedTime）。
/// 未在配置里登记的旧工序/项目排在已登记项之后，保证历史数据也能导出。
/// </summary>
public static class WorkloadExportBuilder
{
    public static List<WorkloadSummaryRow> BuildSummary(
        IEnumerable<WorkloadRecord> records,
        IEnumerable<WorkloadProcess> processes,
        IEnumerable<string>? projectOrder = null)
    {
        var projectRank = BuildProjectRank(projectOrder);
        var processRank = BuildProcessRank(processes);

        return records
            .GroupBy(r => new
            {
                Project = r.ProjectName ?? string.Empty,
                Process = r.ProcessName ?? string.Empty
            })
            .Select(g => new WorkloadSummaryRow
            {
                ProjectName = g.Key.Project,
                ProcessName = g.Key.Process,
                Quantity = g.Sum(x => x.Quantity)
            })
            .OrderBy(r => Rank(projectRank, r.ProjectName))
            .ThenBy(r => Rank(processRank, r.ProjectName, r.ProcessName))
            .ThenBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.ProcessName, StringComparer.Ordinal)
            .ToList();
    }

    public static List<WorkloadDailyRow> BuildDaily(
        IEnumerable<WorkloadRecord> records,
        IEnumerable<WorkloadProcess> processes,
        IEnumerable<string>? projectOrder = null)
    {
        var projectRank = BuildProjectRank(projectOrder);
        var processRank = BuildProcessRank(processes);

        return records
            .GroupBy(r => new
            {
                Date = r.WorkDate.Date,
                Shift = r.Shift ?? string.Empty,
                Project = r.ProjectName ?? string.Empty,
                Process = r.ProcessName ?? string.Empty
            })
            .Select(g => new WorkloadDailyRow
            {
                WorkDate = g.Key.Date,
                Shift = g.Key.Shift,
                ProjectName = g.Key.Project,
                ProcessName = g.Key.Process,
                Quantity = g.Sum(x => x.Quantity)
            })
            .OrderBy(r => r.WorkDate)
            .ThenBy(r => ShiftRank(r.Shift))
            .ThenBy(r => Rank(projectRank, r.ProjectName))
            .ThenBy(r => Rank(processRank, r.ProjectName, r.ProcessName))
            .ThenBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.ProcessName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>明细按 日期 → 班次 → 项目/工序界面顺序 → 执行时间 排序。</summary>
    public static List<WorkloadRecord> OrderDetails(
        IEnumerable<WorkloadRecord> records,
        IEnumerable<WorkloadProcess> processes,
        IEnumerable<string>? projectOrder = null)
    {
        var projectRank = BuildProjectRank(projectOrder);
        var processRank = BuildProcessRank(processes);

        return records
            .OrderBy(r => r.WorkDate.Date)
            .ThenBy(r => ShiftRank(r.Shift))
            .ThenBy(r => Rank(projectRank, r.ProjectName ?? string.Empty))
            .ThenBy(r => Rank(processRank, r.ProjectName ?? string.Empty, r.ProcessName ?? string.Empty))
            .ThenBy(r => r.CreatedTime)
            .ThenBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.ProcessName, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, int> BuildProjectRank(IEnumerable<string>? projectOrder)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        if (projectOrder == null) return rank;

        int i = 0;
        foreach (string p in projectOrder)
        {
            if (string.IsNullOrWhiteSpace(p) || rank.ContainsKey(p)) continue;
            rank[p] = i++;
        }
        return rank;
    }

    private static Dictionary<(string Project, string Process), int> BuildProcessRank(
        IEnumerable<WorkloadProcess> processes)
    {
        var rank = new Dictionary<(string, string), int>();
        int i = 0;
        // 与工作量页一致：Order → CreatedTime
        foreach (var p in processes.OrderBy(x => x.Order).ThenBy(x => x.CreatedTime))
        {
            var key = (p.ProjectName ?? string.Empty, p.ProcessName ?? string.Empty);
            if (rank.ContainsKey(key)) continue;
            rank[key] = i++;
        }
        return rank;
    }

    private static int Rank(Dictionary<string, int> rank, string key)
        => rank.TryGetValue(key, out int value) ? value : int.MaxValue;

    private static int Rank(Dictionary<(string, string), int> rank, string project, string process)
        => rank.TryGetValue((project ?? string.Empty, process ?? string.Empty), out int value)
            ? value
            : int.MaxValue;

    private static int ShiftRank(string? shift)
        => shift == WorkloadRecord.DayShift ? 0
            : shift == WorkloadRecord.NightShift ? 1
            : 2;
}
