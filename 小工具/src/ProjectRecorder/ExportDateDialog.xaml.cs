using System;
using System.Windows;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 导出日期选择小弹窗，默认班次日期。
/// </summary>
public partial class ExportDateDialog : Window
{
    public DateTime SelectedDay { get; private set; } = WorkloadRecord.GetShiftDate(DateTime.Now);

    private readonly InactivityMonitor? _monitor;

    public ExportDateDialog(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        DpDate.SelectedDate = SelectedDay;

        if (_monitor != null)
        {
            PreviewMouseMove += (s, e) => _monitor.NotifyActivity();
            PreviewMouseDown += (s, e) => _monitor.NotifyActivity();
            PreviewMouseUp += (s, e) => _monitor.NotifyActivity();
            PreviewMouseWheel += (s, e) => _monitor.NotifyActivity();
            PreviewKeyDown += (s, e) => _monitor.NotifyActivity();
            PreviewKeyUp += (s, e) => _monitor.NotifyActivity();
            PreviewTextInput += (s, e) => _monitor.NotifyActivity();
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (DpDate.SelectedDate == null)
        {
            MessageBox.Show("请选择日期。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            DpDate.Focus();
            return;
        }

        SelectedDay = DpDate.SelectedDate.Value.Date;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
