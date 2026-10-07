using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 工作量执行明细弹窗：列出某工序在某班次的每次执行时间与增减数量（补录显示“补录”）。
/// </summary>
public partial class WorkDetailDialog : Window
{
    private sealed class Row
    {
        public string Time { get; set; } = string.Empty;
        public string Qty { get; set; } = string.Empty;
    }

    private readonly InactivityMonitor? _monitor;

    public WorkDetailDialog(string project, string process, DateTime date, string shift,
        List<WorkloadRecord> records, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();

        TxtHead.Text = $"{project} · {process}\n{date:yyyy-MM-dd} {shift}";
        var rows = (records ?? new List<WorkloadRecord>())
            .OrderBy(r => r.CreatedTime)
            .Select(r => new Row
            {
                Time = r.IsBackfill ? "补录" : r.CreatedTime.ToString("HH:mm:ss"),
                Qty = r.Quantity > 0 ? $"+{r.Quantity}" : r.Quantity.ToString()
            })
            .ToList();
        LstRows.ItemsSource = rows;

        int total = rows.Count == 0 ? 0 : records.Sum(r => r.Quantity);
        TxtTotal.Text = $"共 {rows.Count} 次，合计 {total}";
    }

    private void HookActivity()
    {
        if (_monitor == null) return;
        PreviewMouseMove += (s, e) => _monitor.NotifyActivity();
        PreviewMouseDown += (s, e) => _monitor.NotifyActivity();
        PreviewMouseUp += (s, e) => _monitor.NotifyActivity();
        PreviewMouseWheel += (s, e) => _monitor.NotifyActivity();
        PreviewKeyDown += (s, e) => _monitor.NotifyActivity();
        PreviewKeyUp += (s, e) => _monitor.NotifyActivity();
        PreviewTextInput += (s, e) => _monitor.NotifyActivity();
    }
}
