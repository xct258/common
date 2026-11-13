using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectRecorderLite.Helpers;
using ProjectRecorderLite.Services;

namespace ProjectRecorderLite;

/// <summary>
/// Lite 版主窗口：左侧项目列表，右侧选中项目的快捷路径。
/// 只有「添加项目」和「快捷路径」两类功能，无登录、无加密。
/// </summary>
public partial class MainWindow : Window
{
    private List<string> _projects = new();
    private bool _suppressSelection;

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSize();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => SaveWindowSize();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshProjects();

        if (!string.IsNullOrEmpty(LiteStore.StartupWarning))
        {
            MessageBox.Show(LiteStore.StartupWarning, "数据提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- 项目 ----------

    private void RefreshProjects(string? keep = null)
    {
        _projects = LiteStore.Projects;

        // 第一项固定为「通用」，其后是项目
        var navItems = new List<string> { LiteStore.GeneralProject };
        navItems.AddRange(_projects);

        _suppressSelection = true;
        LstProjects.ItemsSource = null;
        LstProjects.ItemsSource = navItems;
        _suppressSelection = false;

        TxtProjectEmpty.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (keep != null && (keep == LiteStore.GeneralProject || _projects.Contains(keep)))
        {
            LstProjects.SelectedItem = keep;
            OpenProject(keep);
        }
        else if (_projects.Count == 0)
        {
            ShowNoProject();
        }
    }

    private void LstProjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;

        if (LstProjects.SelectedItem is not string name || string.IsNullOrWhiteSpace(name))
        {
            ShowNoProject();
            return;
        }

        OpenProject(name);
    }

    // 通用与项目共用右侧快捷路径面板；由 ShortcutView 判断是否显示网址栏
    private void OpenProject(string project)
    {
        TxtProjectTitle.Text = project;
        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;
        ViewShortcut.Reload(project);
    }

    private void ShowNoProject()
    {
        TxtNoProject.Visibility = Visibility.Visible;
        PanelProject.Visibility = Visibility.Collapsed;
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddProjectDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        string name = dlg.ProjectName.Trim();
        if (name.Length == 0) return;

        if (name == LiteStore.GeneralProject)
        {
            MessageBox.Show("通用为独立模块，不可作为项目创建。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_projects.Contains(name))
        {
            MessageBox.Show("该项目已存在，已为你选中。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            LstProjects.SelectedItem = name;
            return;
        }

        if (!LiteStore.AddProject(name))
        {
            MessageBox.Show($"保存失败：{LiteStore.LastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshProjects(name);
    }

    // 右键卡片先选中再删；点空白处右键不选中
    private void LstProjects_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void ProjectsMenu_Opened(object sender, RoutedEventArgs e)
    {
        // 「通用」是固定模块，不允许上移/下移/删除
        bool isProject = LstProjects.SelectedItem is string p
                         && !string.IsNullOrWhiteSpace(p)
                         && p != LiteStore.GeneralProject;
        MenuMoveProjectUp.IsEnabled = isProject;
        MenuMoveProjectDown.IsEnabled = isProject;
        MenuDeleteProject.IsEnabled = isProject;
    }

    private void MenuDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (LstProjects.SelectedItem is not string project || string.IsNullOrWhiteSpace(project))
        {
            MessageBox.Show("请先点选要删除的项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (project == LiteStore.GeneralProject)
        {
            MessageBox.Show("通用为固定模块，不能删除。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除项目「{project}」吗？\n其下全部快捷路径将一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        if (!LiteStore.RemoveProject(project))
        {
            MessageBox.Show($"删除失败：{LiteStore.LastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshProjects();
        ShowNoProject();
    }

    private void MenuMoveProjectUp_Click(object sender, RoutedEventArgs e) => MoveProject(-1);
    private void MenuMoveProjectDown_Click(object sender, RoutedEventArgs e) => MoveProject(1);

    private void MoveProject(int delta)
    {
        if (LstProjects.SelectedItem is not string project || string.IsNullOrWhiteSpace(project)) return;
        if (project == LiteStore.GeneralProject) return;

        int i = _projects.IndexOf(project);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _projects.Count) return;

        if (!LiteStore.MoveProject(project, delta))
        {
            MessageBox.Show($"保存失败：{LiteStore.LastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshProjects(project);
    }

    // 空白处松开右键不弹菜单（菜单在松开时触发，只拦按下不够）
    private void ListBlankUp(object sender, MouseButtonEventArgs e)
    {
        if (VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject) == null)
            e.Handled = true;
    }

    // ---------- 窗口尺寸记忆（存注册表，与数据文件无关） ----------

    private void LoadWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ProjectRecorderLite");
            if (key == null) return;
            if (key.GetValue("MainW") is int w && w >= 760 && w <= 1600) Width = w;
            if (key.GetValue("MainH") is int h && h >= 500 && h <= 1000) Height = h;
        }
        catch
        {
            // 读不到就用默认尺寸
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\ProjectRecorderLite");
            key.SetValue("MainW", (int)ActualWidth, RegistryValueKind.DWord);
            key.SetValue("MainH", (int)ActualHeight, RegistryValueKind.DWord);
        }
        catch
        {
            // 存失败不影响退出
        }
    }
}
