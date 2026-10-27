using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 手动复制窗口：展示网址并默认全选，由用户按 Ctrl+C 复制。
/// 程序不提供自动复制，也不会自动写入剪贴板。
/// 弹窗内操作同样上报无操作计时。
/// </summary>
public partial class ManualCopyDialog : Window
{
    private readonly InactivityMonitor? _monitor;

    public ManualCopyDialog(string text, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        TxtContent.Text = text ?? string.Empty;
        _monitor = monitor;
        HookActivity();
        Loaded += (_, _) =>
        {
            TxtContent.Focus();
            TxtContent.SelectAll();
        };
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

    // 获得焦点自动全选，方便直接 Ctrl+C
    private void TxtContent_GotFocus(object sender, RoutedEventArgs e)
    {
        TxtContent.Dispatcher.BeginInvoke(new System.Action(TxtContent.SelectAll));
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
