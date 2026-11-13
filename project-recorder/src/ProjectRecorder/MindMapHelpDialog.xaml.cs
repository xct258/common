using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 思维导图操作教程弹窗：单独按钮唤起，集中说明全部操作。
/// 弹窗内的操作同样上报无操作计时，防止查看中被强制退出。
/// </summary>
public partial class MindMapHelpDialog : Window
{
    private readonly InactivityMonitor? _monitor;

    public MindMapHelpDialog(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();
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

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
