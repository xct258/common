using System.Windows;
using System.Windows.Input;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 思维导图节点编辑弹窗：标题 + 内容两部分；导图画布上只显示标题。
/// 弹窗内的操作同样上报无操作计时，防止输入中被强制退出。
/// </summary>
public partial class MindNodeDialog : Window
{
    public string NodeTitle { get; private set; } = string.Empty;
    public string NodeContent { get; private set; } = string.Empty;
    private readonly InactivityMonitor? _monitor;

    public MindNodeDialog(string title, string content, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        TxtTitle.Text = title ?? string.Empty;
        TxtContent.Text = content ?? string.Empty;
        _monitor = monitor;
        HookActivity();
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                Confirm();
            }
        };
        Loaded += (_, _) =>
        {
            TxtTitle.Focus();
            TxtTitle.SelectAll();
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

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        string title = TxtTitle.Text.Trim();
        if (title.Length == 0)
        {
            MessageBox.Show("请输入标题。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        NodeTitle = title;
        NodeContent = (TxtContent.Text ?? string.Empty).Trim();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
