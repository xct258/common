using System.Windows;
using System.Windows.Media;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 手动复制弹窗：展示文本（默认全选），可手动 Ctrl+C；也提供“复 制”按钮再试一次。
/// 剪贴板被其它程序占用时这是最可靠的兜底。弹窗内操作同样上报无操作计时。
/// </summary>
public partial class ManualCopyDialog : Window
{
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

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

    // 获得焦点自动全选，方便连续多次手动复制
    private void TxtContent_GotFocus(object sender, RoutedEventArgs e)
    {
        TxtContent.Dispatcher.BeginInvoke(new System.Action(TxtContent.SelectAll));
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.CopyToClipboard(TxtContent.Text))
        {
            TxtStatus.Foreground = OkBrush;
            TxtStatus.Text = "已复制到剪贴板。";
            return;
        }

        TxtStatus.Foreground = FailBrush;
        TxtStatus.Text = "复制失败，请手动选中后按 Ctrl+C。";
        TxtContent.Focus();
        TxtContent.SelectAll();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
