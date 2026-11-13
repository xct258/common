using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 「其它」工作量录入弹窗：项目自由填写 + 时间（小时）。
/// 弹窗内的操作同样上报无操作计时，防止输入中被强制退出。
/// </summary>
public partial class OtherWorkDialog : Window
{
    public string ProjectName { get; private set; } = string.Empty;
    public int Hours { get; private set; }
    private readonly InactivityMonitor? _monitor;

    public OtherWorkDialog(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();
        Loaded += (_, _) => TxtProject.Focus();
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
        string project = TxtProject.Text.Trim();
        if (project.Length == 0)
        {
            MessageBox.Show("请输入项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtProject.Focus();
            return;
        }

        if (!int.TryParse(TxtHours.Text.Trim(), out int hours) || hours <= 0 || hours > 999)
        {
            MessageBox.Show("请输入 1~999 之间的小时数。", "校验",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtHours.Focus();
            TxtHours.SelectAll();
            return;
        }

        ProjectName = project;
        Hours = hours;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
