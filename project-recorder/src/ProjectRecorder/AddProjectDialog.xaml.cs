using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 添加项目小弹窗：项目名称（必填）。
/// 弹窗内的操作同样上报无操作计时，防止输入中被强制退出。
/// </summary>
public partial class AddProjectDialog : Window
{
    public string ProjectName { get; private set; } = string.Empty;
    private readonly InactivityMonitor? _monitor;

    public AddProjectDialog(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();
        TxtName.Focus();
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
        string name = TxtName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("请输入项目名称。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        ProjectName = name;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
