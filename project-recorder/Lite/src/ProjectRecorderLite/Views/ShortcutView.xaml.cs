using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProjectRecorderLite.Helpers;
using ProjectRecorderLite.Models;
using ProjectRecorderLite.Services;

namespace ProjectRecorderLite.Views;

/// <summary>
/// 快捷路径视图：固定「通用」模块支持文件夹 + 网址，项目内仅文件夹。
/// 文件夹单击用资源管理器打开；网址单击打开手动复制窗口（不再自动写入剪贴板）。
/// </summary>
public partial class ShortcutView : UserControl
{
    private readonly List<PathShortcut> _shortcuts = new();
    private string? _project;

    private const string GeneralHint = "文件夹：单击用资源管理器打开；网址：单击打开手动复制窗口（全选后按 Ctrl+C）";
    private const string ProjectHint = "文件夹：单击用资源管理器打开";

    public ShortcutView()
    {
        InitializeComponent();
    }

    private string HintDefault()
        => _project == LiteStore.GeneralProject ? GeneralHint : ProjectHint;

    /// <summary>按当前归属（通用或项目）重新加载快捷路径。</summary>
    public void Reload(string? project)
    {
        _project = project;
        _shortcuts.Clear();

        if (!string.IsNullOrWhiteSpace(project))
            _shortcuts.AddRange(LiteStore.GetShortcuts(project!));

        bool isGeneral = project == LiteStore.GeneralProject;

        var folders = _shortcuts.Where(x => !x.IsUrl).ToList();
        // 网址快捷路径只在「通用」显示（项目内只显示文件夹）
        var urls = isGeneral
            ? _shortcuts.Where(x => x.IsUrl).ToList()
            : new List<PathShortcut>();

        UrlShortcutPanel.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        ColShortcutGap.Width = isGeneral ? new GridLength(14) : new GridLength(0);
        ColShortcutUrl.Width = isGeneral ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        LstFolderShortcuts.ItemsSource = null;
        LstFolderShortcuts.ItemsSource = folders;
        TxtFolderShortcutEmpty.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LstUrlShortcuts.ItemsSource = null;
        LstUrlShortcuts.ItemsSource = urls;
        TxtUrlShortcutEmpty.Visibility = urls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        TxtShortcutHint.Text = HintDefault();
    }

    private void BtnAddShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_project)) return;

        bool allowUrl = _project == LiteStore.GeneralProject;
        var dlg = new PathShortcutDialog(allowUrl) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        if (_shortcuts.Any(x => x.Name == dlg.ShortcutName && x.FolderPath == dlg.ShortcutPath))
        {
            MessageBox.Show("同名同地址的快捷方式已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var shortcut = new PathShortcut
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _project!,
            Kind = dlg.ShortcutKind,
            Name = dlg.ShortcutName,
            FolderPath = dlg.ShortcutPath,
            CreatedTime = DateTime.Now
        };

        if (!LiteStore.AddShortcut(shortcut))
        {
            MessageBox.Show($"保存失败：{LiteStore.LastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Reload(_project);
    }

    private void ShortcutFolder_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PathShortcut sc) return;

        string target = (sc.FolderPath ?? string.Empty).Trim();
        if (target.Length == 0) return;
        if (!Directory.Exists(target) && !File.Exists(target))
        {
            MessageBox.Show($"路径不存在：\n{target}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShortcutUrl_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PathShortcut sc) OpenManualCopy(sc);
    }

    private void MenuManualCopyUrlShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (LstUrlShortcuts.SelectedItem is PathShortcut sc) OpenManualCopy(sc);
    }

    /// <summary>打开手动复制窗口：不自动写剪贴板，由用户全选后按 Ctrl+C 复制。</summary>
    private void OpenManualCopy(PathShortcut shortcut)
    {
        string url = (shortcut.FolderPath ?? string.Empty).Trim();
        if (url.Length == 0) return;

        new ManualCopyDialog(url) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void LstShortcuts_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void ListBlankUp(object sender, MouseButtonEventArgs e)
    {
        if (VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject) == null)
            e.Handled = true;
    }

    private void MenuDeleteFolderShortcut_Click(object sender, RoutedEventArgs e)
        => Delete(LstFolderShortcuts.SelectedItem as PathShortcut);

    private void MenuDeleteUrlShortcut_Click(object sender, RoutedEventArgs e)
        => Delete(LstUrlShortcuts.SelectedItem as PathShortcut);

    private void Delete(PathShortcut? sel)
    {
        if (sel == null)
        {
            MessageBox.Show("请先右键点选要删除的快捷路径。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除快捷路径「{sel.Name}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        if (!LiteStore.RemoveShortcut(sel.Id))
        {
            MessageBox.Show($"删除失败：{LiteStore.LastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Reload(_project);
    }
}
