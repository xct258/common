using System.Windows;

namespace ProjectRecorderLite;

/// <summary>
/// 手动复制窗口：展示网址并默认全选，由用户按 Ctrl+C 复制。
/// 程序不提供自动复制，也不会自动写入剪贴板。
/// </summary>
public partial class ManualCopyDialog : Window
{
    public ManualCopyDialog(string text)
    {
        InitializeComponent();
        TxtContent.Text = text ?? string.Empty;
        Loaded += (_, _) =>
        {
            TxtContent.Focus();
            TxtContent.SelectAll();
        };
    }

    // 获得焦点自动全选，方便直接 Ctrl+C
    private void TxtContent_GotFocus(object sender, RoutedEventArgs e)
    {
        TxtContent.Dispatcher.BeginInvoke(new System.Action(TxtContent.SelectAll));
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
