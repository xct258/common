using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>同名文件处理方式。</summary>
public enum DuplicateChoice
{
    Overwrite,
    KeepBoth,
    Skip
}

/// <summary>
/// 导入同名文件时的处理弹窗：覆盖旧文件 / 保留两者 / 跳过，可勾选“后续同名都这样处理”。
/// 关右上角 X 或按 Esc 按跳过本次处理。
/// </summary>
public partial class DuplicateFileDialog : Window
{
    public DuplicateChoice Choice { get; private set; } = DuplicateChoice.Skip;
    public bool ApplyToAll => ChkApplyAll.IsChecked == true;

    public DuplicateFileDialog(string fileName, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        TxtMessage.Text = $"文件「{fileName}」已存在，如何处理？\n覆盖将替换旧文件内容（保留首次导入时间，更新时间记为现在）。";
        if (monitor == null) return;
        PreviewMouseMove += (s, e) => monitor.NotifyActivity();
        PreviewMouseDown += (s, e) => monitor.NotifyActivity();
        PreviewMouseUp += (s, e) => monitor.NotifyActivity();
        PreviewMouseWheel += (s, e) => monitor.NotifyActivity();
        PreviewKeyDown += (s, e) => monitor.NotifyActivity();
        PreviewKeyUp += (s, e) => monitor.NotifyActivity();
        PreviewTextInput += (s, e) => monitor.NotifyActivity();
    }

    private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
    {
        Choice = DuplicateChoice.Overwrite;
        DialogResult = true;
        Close();
    }

    private void BtnKeepBoth_Click(object sender, RoutedEventArgs e)
    {
        Choice = DuplicateChoice.KeepBoth;
        DialogResult = true;
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        Choice = DuplicateChoice.Skip;
        DialogResult = true;
        Close();
    }
}
