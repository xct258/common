using System;
using System.IO;
using System.Windows;
using ProjectRecorderLite.Models;

namespace ProjectRecorderLite;

/// <summary>
/// 添加快捷路径弹窗：名称 + 类型（文件夹 / 网址）。
/// 文件夹可浏览选择；网址自动补 https:// 前缀。
/// </summary>
public partial class PathShortcutDialog : Window
{
    public string ShortcutName { get; private set; } = string.Empty;
    public string ShortcutPath { get; private set; } = string.Empty;
    public string ShortcutKind { get; private set; } = PathShortcut.KindFolder;

    public PathShortcutDialog(bool allowUrl = true)
    {
        InitializeComponent();
        if (!allowUrl)
        {
            // 项目内只支持文件夹快捷路径：隐藏类型选择，锁定文件夹
            TxtKindLabel.Visibility = Visibility.Collapsed;
            KindPanel.Visibility = Visibility.Collapsed;
            RdoFolder.IsChecked = true;
        }
        TxtName.Focus();
    }

    private bool IsUrlSelected => RdoUrl.IsChecked == true;

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        // XAML 初始化阶段也可能触发 Checked，此时其它控件还没建好；默认态已是“文件夹”无需处理
        if (!IsInitialized || TxtTargetLabel == null || BtnBrowse == null || TxtHint == null) return;

        bool url = IsUrlSelected;
        TxtTargetLabel.Text = url ? "网址 *" : "文件夹路径 *";
        BtnBrowse.Visibility = url ? Visibility.Collapsed : Visibility.Visible;
        TxtHint.Visibility = url ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要快速打开的文件夹",
            ShowNewFolderButton = false
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        TxtPath.Text = dlg.SelectedPath;
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            string name = Path.GetFileName(dlg.SelectedPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name)) TxtName.Text = name;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("请输入名称。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        string target = TxtPath.Text.Trim().Trim('"').Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(IsUrlSelected ? "请输入网址。" : "请输入路径。", "校验",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPath.Focus();
            return;
        }

        if (IsUrlSelected)
        {
            // 没写协议就补 https://（如 www.example.com）
            if (!target.Contains("://")) target = "https://" + target;
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !uri.Host.Contains('.'))
            {
                MessageBox.Show("网址格式不正确，请填写如 https://www.example.com 的网址。", "校验",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPath.Focus();
                return;
            }
            target = uri.AbsoluteUri;
            ShortcutKind = PathShortcut.KindUrl;
        }
        else
        {
            ShortcutKind = PathShortcut.KindFolder;
        }

        ShortcutName = name;
        ShortcutPath = target;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
