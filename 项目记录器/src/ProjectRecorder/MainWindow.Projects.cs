using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// MainWindow 拆分文件：项目列表：加载/选中/新增/删除/排序
/// 与 MainWindow.xaml.cs 同为一个 partial 类，可直接访问其私有字段和 x:Name 生成的控件字段。
/// </summary>
public partial class MainWindow
{
    // ---------- 项目 ----------

    private void RefreshProjects(string? keep = null)
    {
        var loaded = _data.LoadProjects();
        if (loaded == null)
        {
            Close();
            return;
        }
        _projects = loaded;

        // 第一项固定为「通用」，其后是项目
        var navItems = new List<string> { DataStore.FixedMachineProject };
        navItems.AddRange(_projects);
        LstProjects.ItemsSource = null;
        LstProjects.ItemsSource = navItems;
        if (keep != null && _projects.Contains(keep))
            LstProjects.SelectedItem = keep;
        TxtProjectEmpty.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LstProjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstProjects.SelectedItem is not string name || string.IsNullOrWhiteSpace(name)) return;
        if (name == DataStore.FixedMachineProject)
            OpenGeneralWorkload();
        else
            OpenProject(name);
    }

    // 选中项目：右侧面板显示（切换项目不记忆上次板块，默认进工作量）
    private void OpenProject(string project)
    {
        _monitor.NotifyActivity();
        _currentProject = project;
        ViewProcessTab.ResetSelection();

        BtnHome.IsChecked = false;
        ProjectTabs.Visibility = Visibility.Visible;
        PageHeader.Visibility = Visibility.Collapsed;
        BtnTabProcess.Visibility = Visibility.Visible;
        BtnTabWorkload.Visibility = Visibility.Visible;
        BtnTabShortcuts.Visibility = Visibility.Visible;

        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;

        BtnTabProcess.IsChecked = false;
        BtnTabShortcuts.IsChecked = false;
        BtnTabWorkload.IsChecked = true;
        ShowWorkloadTab();
    }

    // 左栏底部按钮：弹窗输入项目名
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        var dlg = new AddProjectDialog(_monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AddProject(dlg.ProjectName);
    }

    private void AddProject(string name)
    {
        _monitor.NotifyActivity();
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return;
        if (name == DataStore.FixedMachineProject)
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

        _projects.Add(name);
        if (!_data.SaveProjects(_projects))
        {
            _projects.Remove(name);
            return;
        }

        RefreshProjects(name);
    }

    // 右键卡片先选中再删；点空白处右键不选中
    private void LstProjects_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    // 右键菜单：只有真正的项目才允许上移/下移/删除（「工作量」是固定入口）
    private void ProjectsMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool isProject = LstProjects.SelectedItem is string p && !string.IsNullOrWhiteSpace(p);
        MenuMoveProjectUp.IsEnabled = isProject;
        MenuMoveProjectDown.IsEnabled = isProject;
        MenuDeleteProject.IsEnabled = isProject;
    }

    private void MenuDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstProjects.SelectedItem is not string project || string.IsNullOrWhiteSpace(project))
        {
            MessageBox.Show("请先点选要删除的项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除项目「{project}」吗？\n其下全部工序、操作流程、工作量记录及快捷路径将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        _projects.Remove(project);
        _data.DeleteProject(project, _projects);
        RefreshProjects();
        ShowNoProject();
    }

    private void MenuMoveProjectUp_Click(object sender, RoutedEventArgs e) => MoveProject(-1);
    private void MenuMoveProjectDown_Click(object sender, RoutedEventArgs e) => MoveProject(1);

    private void MoveProject(int delta)
    {
        _monitor.NotifyActivity();
        if (LstProjects.SelectedItem is not string project || string.IsNullOrWhiteSpace(project)) return;
        int i = _projects.IndexOf(project);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _projects.Count) return;
        (_projects[i], _projects[j]) = (_projects[j], _projects[i]);
        if (!_data.SaveProjects(_projects))
        {
            RefreshProjects(project);
            return;
        }
        RefreshProjects(project);
    }
}
