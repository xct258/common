using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 通用模块「导出」页：默认导出本班次，也可选历史日期或日期范围批量导出（xlsx）。
/// 每个日期一个工作表，工作表内同时包含当日汇总和明细；工序顺序与工作量界面一致。
/// </summary>
public partial class ExportView : UserControl
{
    private DataService? _data;
    private InactivityMonitor? _monitor;
    private DateTime _exportDate = DateTime.Today;
    private bool _exportToday = true;

    public ExportView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>进入导出页：重置为“今日（本班次）”，批量范围默认最近 7 天。</summary>
    public void Reset()
    {
        _exportToday = true;
        _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);
        RefreshDateButton();

        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        BatchStart.DisplayDateEnd = today;
        BatchEnd.DisplayDateEnd = today;
        if (!BatchStart.SelectedDate.HasValue) BatchStart.SelectedDate = today.AddDays(-6);
        if (!BatchEnd.SelectedDate.HasValue) BatchEnd.SelectedDate = today;
    }

    // 通用「导出」入口：默认导出本班次；选日期后导出该日期全天合计（xlsx）
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;

        DateTime day;
        string? shift = null;
        string label;
        string fileName;
        if (_exportToday)
        {
            var now = DateTime.Now;
            day = WorkloadRecord.GetShiftDate(now);
            shift = WorkloadRecord.GetShiftName(now);
            label = $"{day:yyyy-MM-dd} {shift}";
            fileName = $"工作量_{day:yyyy-MM-dd}_{shift}.xlsx";
        }
        else
        {
            day = _exportDate.Date;
            label = $"{day:yyyy-MM-dd}";
            fileName = $"工作量_{day:yyyy-MM-dd}.xlsx";
        }

        var all = _data.LoadWorkload();
        if (all == null) return;
        var list = all
            .Where(x => x.WorkDate.Date == day.Date && (shift == null || x.Shift == shift))
            .ToList();

        if (list.Count == 0)
        {
            MessageBox.Show($"{label} 暂无工作量记录。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fileDlg = new SaveFileDialog
        {
            Title = "导出工作量",
            Filter = "Excel 文件|*.xlsx",
            FileName = fileName
        };
        var owner = Window.GetWindow(this);
        bool? picked = owner == null ? fileDlg.ShowDialog() : fileDlg.ShowDialog(owner);
        if (picked != true) return;

        try
        {
            var (processes, projectOrder) = LoadOrderContext();
            int rowCount = ExcelExporter.ExportWorkload(
                fileDlg.FileName, day, list, shift, processes, projectOrder);
            MessageBox.Show($"导出成功，共 {rowCount} 项合计：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 批量导出：日期范围内每天一个工作表，每个工作表包含当日汇总和明细
    private void BtnBatchExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;

        if (!BatchStart.SelectedDate.HasValue || !BatchEnd.SelectedDate.HasValue)
        {
            MessageBox.Show("请选择开始日期和结束日期。", "批量导出",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime start = BatchStart.SelectedDate.Value.Date;
        DateTime end = BatchEnd.SelectedDate.Value.Date;
        if (start > end)
        {
            MessageBox.Show("开始日期不能晚于结束日期。", "批量导出",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (end > today) end = today;
        if (start > today)
        {
            MessageBox.Show("开始日期不能晚于今天。", "批量导出",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if ((end - start).TotalDays > 366)
        {
            MessageBox.Show("单次批量导出最多支持 366 天，请缩小日期范围。", "批量导出",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? shift = BatchShift.SelectedIndex switch
        {
            1 => WorkloadRecord.DayShift,
            2 => WorkloadRecord.NightShift,
            _ => null
        };

        var all = _data.LoadWorkload();
        if (all == null) return;
        var list = all
            .Where(x => x.WorkDate.Date >= start && x.WorkDate.Date <= end)
            .Where(x => shift == null || x.Shift == shift)
            .ToList();

        if (list.Count == 0)
        {
            string range = start == end ? $"{start:yyyy-MM-dd}" : $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
            MessageBox.Show($"{range}{(shift == null ? "" : " " + shift)} 暂无工作量记录。", "批量导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string shiftSuffix = shift == null ? string.Empty : $"_{shift}";
        var fileDlg = new SaveFileDialog
        {
            Title = "批量导出工作量",
            Filter = "Excel 文件|*.xlsx",
            FileName = $"工作量_{start:yyyy-MM-dd}_{end:yyyy-MM-dd}{shiftSuffix}.xlsx"
        };
        var owner = Window.GetWindow(this);
        bool? picked = owner == null ? fileDlg.ShowDialog() : fileDlg.ShowDialog(owner);
        if (picked != true) return;

        try
        {
            var (processes, projectOrder) = LoadOrderContext();
            int rowCount = ExcelExporter.ExportWorkloadBatch(
                fileDlg.FileName, start, end, shift, list, processes, projectOrder);
            int dayCount = list.Select(x => x.WorkDate.Date).Distinct().Count();
            MessageBox.Show(
                $"导出成功：{dayCount} 天（每天一个工作表），{rowCount} 条工序合计，{list.Count} 条明细。\n{fileDlg.FileName}",
                "批量导出", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>项目按左侧列表顺序（通用固定第一），工序按工作量页 Order → CreatedTime。</summary>
    private (List<WorkloadProcess> Processes, List<string> ProjectOrder) LoadOrderContext()
    {
        var processes = _data?.LoadWorkloadProcesses() ?? new List<WorkloadProcess>();
        var projects = _data?.LoadProjects() ?? new List<string>();

        var projectOrder = new List<string> { DataStore.FixedMachineProject };
        foreach (string p in projects)
        {
            if (string.Equals(p, DataStore.FixedMachineProject, StringComparison.Ordinal)) continue;
            projectOrder.Add(p);
        }
        return (processes, projectOrder);
    }

    // 导出页：默认“今日”（本班次日期），上限封顶到班次今日，不能选未来
    private void RefreshDateButton()
    {
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        ExpPickDate.Content = _exportToday ? "今日" : _exportDate.ToString("yyyy-MM-dd");
        ExpNextDay.IsEnabled = !_exportToday;
    }

    private void ExpPrevDay_Click(object sender, RoutedEventArgs e) => ChangeDate(-1);
    private void ExpNextDay_Click(object sender, RoutedEventArgs e) => ChangeDate(1);

    private void ChangeDate(int days)
    {
        _monitor?.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        DateTime baseDay = _exportToday ? today : _exportDate.Date;
        if (baseDay > today) baseDay = today;
        DateTime next = baseDay.AddDays(days);
        if (next >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        else
        {
            _exportToday = false;
            _exportDate = next;
        }
        RefreshDateButton();
    }

    private void ExpPickDate_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date > today.Date)
        {
            _exportToday = true;
            _exportDate = today;
            RefreshDateButton();
        }
        try
        {
            ExpCal.DisplayDateStart = null;
            ExpCal.DisplayDateEnd = today;
            ExpCal.BlackoutDates.Clear();
            if (today.Date < new DateTime(9999, 12, 31))
                ExpCal.BlackoutDates.Add(new CalendarDateRange(today.Date.AddDays(1), new DateTime(9999, 12, 31)));
        }
        catch
        {
            // 日历限制设置失败不影响打开
        }
        ExpCal.SelectedDate = _exportToday ? today : _exportDate;
        ExpCal.DisplayDate = ExpCal.SelectedDate ?? today;
        if (ExpCal.DisplayDate > today) ExpCal.DisplayDate = today;
        ExpPopCalendar.IsOpen = true;
    }

    private void ExpCal_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (ExpCal.SelectedDate.HasValue)
        {
            DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
            DateTime picked = ExpCal.SelectedDate.Value.Date;
            if (picked >= today.Date)
            {
                _exportToday = true;
                _exportDate = today;
            }
            else
            {
                _exportDate = picked;
                _exportToday = false;
            }
            RefreshDateButton();
        }
        ExpPopCalendar.IsOpen = false;
    }
}
