using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProjectRecorder.Helpers;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 快捷路径视图：通用模块含文件夹 + 网址，项目内仅文件夹。
/// 文件夹单击用资源管理器打开；网址单击打开手动复制窗口（不再自动写入剪贴板）。
/// </summary>
public partial class ShortcutView : UserControl
{
    private readonly List<PathShortcut> _shortcuts = new();
    private DataService? _data;
    private InactivityMonitor? _monitor;
    private string? _project;

    public ShortcutView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>按当前项目重新加载快捷路径（通用模块或项目各一份）。</summary>
    public void Reload(string? project)
    {
        _project = project;
        if (_data == null) return;

        var all = _data.LoadShortcuts();
        if (all == null) return;

        string owner = _project ?? DataStore.FixedMachineProject;
        bool isGeneral = owner == DataStore.FixedMachineProject;
        _shortcuts.Clear();
        _shortcuts.AddRange(all.Where(x =>
        {
            // 旧数据没有 ProjectName，视为通用模块
            string p = string.IsNullOrWhiteSpace(x.ProjectName) ? DataStore.FixedMachineProject : x.ProjectName;
            return p == owner;
        }));

        var folders = _shortcuts.Where(x => !x.IsUrl).ToList();
        // 网址快捷路径只在通用模块显示（项目内只显示文件夹）
        var urls = isGeneral
            ? _shortcuts.Where(x => x.IsUrl).ToList()
            : new List<PathShortcut>();

        UrlShortcutPanel.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        ColShortcutGap.Width = isGeneral ? new GridLength(14) : new GridLength(0);
        ColShortcutUrl.Width = isGeneral ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TxtShortcutHint.Text = HintDefault();

        LstFolderShortcuts.ItemsSource = null;
        LstFolderShortcuts.ItemsSource = folders;
        TxtFolderShortcutEmpty.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LstUrlShortcuts.ItemsSource = null;
        LstUrlShortcuts.ItemsSource = urls;
        TxtUrlShortcutEmpty.Visibility = urls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string HintDefault()
        => _project == DataStore.FixedMachineProject
            ? "文件夹：单击用资源管理器打开；网址：单击打开手动复制窗口（全选后按 Ctrl+C）"
            : "文件夹：单击用资源管理器打开";

    // 统一添加入口：弹窗内选名称 + 类型（项目内只能选文件夹）
    private void BtnAddShortcut_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_project) || _data == null) return;

        bool allowUrl = _project == DataStore.FixedMachineProject;
        var dlg = new PathShortcutDialog(_monitor!, allowUrl) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        if (_shortcuts.Any(x => x.Name == dlg.ShortcutName && x.FolderPath == dlg.ShortcutPath))
        {
            MessageBox.Show("同名同地址的快捷方式已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var all = _data.LoadShortcuts();
        if (all == null) return;
        all.Add(new PathShortcut
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _project!,
            Kind = dlg.ShortcutKind,
            Name = dlg.ShortcutName,
            FolderPath = dlg.ShortcutPath,
            CreatedTime = DateTime.Now
        });
        if (!_data.SaveShortcuts(all)) return;

        Reload(_project);
    }

    // 文件夹卡片：单击用资源管理器打开
    private void ShortcutFolder_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor?.NotifyActivity();
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

    // 网址卡片：单击打开手动复制窗口（不自动写剪贴板、不打开浏览器）
    private void ShortcutUrl_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor?.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is PathShortcut sc) OpenManualCopy(sc);
    }

    private void MenuManualCopyUrlShortcut_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstUrlShortcuts.SelectedItem is PathShortcut sc) OpenManualCopy(sc);
    }

    /// <summary>打开手动复制窗口：不自动写剪贴板，由用户全选后按 Ctrl+C 复制。</summary>
    private void OpenManualCopy(PathShortcut shortcut)
    {
        string url = (shortcut.FolderPath ?? string.Empty).Trim();
        if (url.Length == 0) return;

        new ManualCopyDialog(url, _monitor!) { Owner = Window.GetWindow(this) }.ShowDialog();
        _monitor?.NotifyActivity();
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

    // 空白处松开右键不弹菜单（菜单在松开时触发，只拦按下不够）
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
        _monitor?.NotifyActivity();
        if (sel == null)
        {
            MessageBox.Show("请先右键点选要删除的快捷路径。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除快捷路径「{sel.Name}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        if (_data == null) return;
        var all = _data.LoadShortcuts();
        if (all == null) return;
        all.RemoveAll(x => x.Id == sel.Id);
        if (!_data.SaveShortcuts(all, "删除失败")) return;
        Reload(_project);
    }
}
