using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 通用名称输入弹窗（标题/说明/初始值可配）：用于思维导图的新建与重命名。
/// 弹窗内的操作同样上报无操作计时，防止输入中被强制退出。
/// </summary>
public partial class NameInputDialog : Window
{
    public string Value { get; private set; } = string.Empty;
    private readonly InactivityMonitor? _monitor;

    public NameInputDialog(string title, string label, string initial, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        Title = title;
        TxtLabel.Text = label;
        TxtValue.Text = initial ?? string.Empty;
        _monitor = monitor;
        HookActivity();
        Loaded += (_, _) =>
        {
            TxtValue.Focus();
            TxtValue.SelectAll();
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

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string value = TxtValue.Text.Trim();
        if (value.Length == 0)
        {
            MessageBox.Show("请输入名称。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtValue.Focus();
            return;
        }

        Value = value;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
