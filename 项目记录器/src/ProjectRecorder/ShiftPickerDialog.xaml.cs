using System;
using System.Windows;
using System.Windows.Controls;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 补录工作量弹窗：只选日期 + 班次（不选时间），且只能选当前班次**之前**的班次。
/// 补录记录在导出明细里标记为“补录”。弹窗内的操作同样上报无操作计时。
/// </summary>
public partial class ShiftPickerDialog : Window
{
    /// <summary>补录日期（班次日期）。</summary>
    public DateTime SelectedDate { get; private set; }
    /// <summary>补录班次（白班 / 夜班）。</summary>
    public string SelectedShift { get; private set; } = WorkloadRecord.DayShift;

    private readonly InactivityMonitor? _monitor;
    private readonly DateTime _curDate;
    private readonly int _curOrder;

    public ShiftPickerDialog(DateTime now, string currentShift, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();

        _curDate = WorkloadRecord.GetShiftDate(now);
        _curOrder = currentShift == WorkloadRecord.NightShift ? 1 : 0;

        CmbShift.Items.Add(WorkloadRecord.DayShift);
        CmbShift.Items.Add(WorkloadRecord.NightShift);

        // 最近可选 = 当前班次的前一个班次：夜班→当天白班；白班→前一天夜班
        DateTime latestDate;
        string latestShift;
        if (currentShift == WorkloadRecord.NightShift)
        {
            latestDate = _curDate;
            latestShift = WorkloadRecord.DayShift;
        }
        else
        {
            latestDate = _curDate.AddDays(-1);
            latestShift = WorkloadRecord.NightShift;
        }

        try
        {
            DtDate.DisplayDateStart = null;
            DtDate.DisplayDateEnd = latestDate;
            DtDate.BlackoutDates.Clear();
            if (latestDate.Date < new DateTime(9999, 12, 31))
                DtDate.BlackoutDates.Add(new CalendarDateRange(latestDate.Date.AddDays(1), new DateTime(9999, 12, 31)));
        }
        catch
        {
            // 日历范围设置失败不影响使用（确定时仍会校验）
        }

        DtDate.SelectedDate = latestDate;
        DtDate.DisplayDate = latestDate;
        CmbShift.SelectedIndex = latestShift == WorkloadRecord.NightShift ? 1 : 0;
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

    private void DtDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        => _monitor?.NotifyActivity();

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (DtDate.SelectedDate == null)
        {
            MessageBox.Show("请选择日期。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (CmbShift.SelectedIndex < 0)
        {
            MessageBox.Show("请选择班次。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime date = DtDate.SelectedDate.Value.Date;
        string shift = CmbShift.SelectedIndex == 1 ? WorkloadRecord.NightShift : WorkloadRecord.DayShift;
        int order = shift == WorkloadRecord.NightShift ? 1 : 0;

        // 只能补录当前班次之前的班次（不能选当前或以后）
        bool valid = date < _curDate || (date == _curDate && order < _curOrder);
        if (!valid)
        {
            MessageBox.Show("补录只能选择当前班次之前的班次，不能选择当前或以后的班次。",
                "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedDate = date;
        SelectedShift = shift;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
