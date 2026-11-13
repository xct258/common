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
/// 主窗口：左侧常驻「通用模块入口 + 项目列表」，右侧为选中项的面板。
/// 通用已独立：含「工作量 / 快捷路径」；项目面板为「工序 / 工作量 / 快捷路径」标签页。
/// </summary>
public partial class MainWindow : Window
{
    private enum GeneralTab { Home, Export, Note }

    private readonly DataService _data;
    private List<string> _projects = new();
    private MindMapWindow? _mindMapWindow;
    private FileVaultWindow? _fileVaultWindow;
    private string? _currentProject;
    private GeneralTab _generalTab = GeneralTab.Home;
    private string _shiftKey = string.Empty;
    private bool _autoLogin;
    private bool _autoLoginUiLoading;
    private readonly InactivityMonitor _monitor = new();

    private static readonly Regex NumRegex = new("^[0-9]+$");

    public MainWindow()
    {
        InitializeComponent();
        string password = AuthService.SessionPassword
            ?? throw new InvalidOperationException("未登录，拒绝访问。");
        _data = new DataService(password);
        _data.Error += (message, title, _) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        ViewNoteTab.Initialize(_data, _monitor);
        ViewShortcutTab.Initialize(_data, _monitor);
        ViewExportTab.Initialize(_data, _monitor);
        ViewWorkloadTab.Initialize(_data, _monitor);
        ViewProcessTab.Initialize(_data, _monitor);
        LoadWindowSize();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { ViewNoteTab.SaveNow(); SaveWindowSize(); _monitor.Dispose(); };
    }

    // 主页按钮：功能卡片主页（导出工作量 / 思维导图 / 笔记 / 加密文件）
    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {
        OpenHome();
    }

    private void OpenHome()
    {
        _monitor.NotifyActivity();
        _currentProject = DataStore.FixedMachineProject;
        ViewProcessTab.ResetSelection();

        BtnHome.IsChecked = true;
        LstProjects.SelectedItem = null;

        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;
        ShowGeneralHome();
    }

    // 左侧「通用」：与项目相同的上方标签栏，含「工作量 / 快捷路径」板块（全局归属）
    private void OpenGeneralWorkload()
    {
        _monitor.NotifyActivity();
        _currentProject = DataStore.FixedMachineProject;
        ViewProcessTab.ResetSelection();

        BtnHome.IsChecked = false;
        ProjectTabs.Visibility = Visibility.Visible;
        PageHeader.Visibility = Visibility.Collapsed;
        BtnTabProcess.Visibility = Visibility.Collapsed;
        BtnTabWorkload.Visibility = Visibility.Visible;
        BtnTabShortcuts.Visibility = Visibility.Visible;

        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;

        BtnTabWorkload.IsChecked = true;
        ShowWorkloadTab();
    }

    private void ShowNoProject()
    {
        _currentProject = null;
        ViewProcessTab.ResetSelection();
        BtnHome.IsChecked = false;
        PanelProject.Visibility = Visibility.Collapsed;
        TxtNoProject.Visibility = Visibility.Visible;
    }

    private void TabProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowProcessTab();
    }

    private void TabWorkload_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowWorkloadTab();
    }

    private void TabShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowShortcutTab();
    }

    // 快捷路径视图：通用与项目共用（按 _currentProject 过滤）
    private void ShowShortcutTab()
    {
        ViewNoteTab.SaveNow();
        HideAllViews();
        ViewShortcutTab.Visibility = Visibility.Visible;
        ViewShortcutTab.Reload(_currentProject);
    }

    // 工作量视图：进入时回到当前班次（跨班次自动刷新也走这里）
    private void ShowWorkloadTab()
    {
        ViewNoteTab.SaveNow();
        HideAllViews();
        ViewWorkloadTab.Visibility = Visibility.Visible;
        _shiftKey = ShiftService.CurrentKey();
        ViewWorkloadTab.Reload(_currentProject);
    }

    // 工序视图：同项目内保留上次选中的工序
    private void ShowProcessTab()
    {
        ViewNoteTab.SaveNow();
        HideAllViews();
        ViewProcessTab.Visibility = Visibility.Visible;
        ViewProcessTab.Reload(_currentProject);
    }

    // ---------- 主页功能入口（卡片 → 各功能独立整页） ----------

    private void GTabExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        _generalTab = GeneralTab.Export;
        ShowGeneralTab();
    }

    private void GTabMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        OpenMindMapWindow();
    }

    private void GTabNote_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        _generalTab = GeneralTab.Note;
        ShowGeneralTab();
    }

    private void GTabFiles_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        OpenFileVaultWindow();
    }

    // 思维导图独立窗口（顶部按项目分组，可与主窗口同时操作）
    private void OpenMindMapWindow()
    {
        if (_mindMapWindow != null && _mindMapWindow.IsLoaded)
        {
            if (_mindMapWindow.WindowState == WindowState.Minimized)
                _mindMapWindow.WindowState = WindowState.Normal;
            _mindMapWindow.Activate();
            return;
        }
        _mindMapWindow = new MindMapWindow(_monitor) { Owner = this };
        _mindMapWindow.Closed += (_, _) => _mindMapWindow = null;
        _mindMapWindow.Show();
    }

    // 加密文件独立窗口（顶部按项目分组，可与主窗口同时操作）
    private void OpenFileVaultWindow()
    {
        if (_fileVaultWindow != null && _fileVaultWindow.IsLoaded)
        {
            if (_fileVaultWindow.WindowState == WindowState.Minimized)
                _fileVaultWindow.WindowState = WindowState.Normal;
            _fileVaultWindow.Activate();
            return;
        }
        _fileVaultWindow = new FileVaultWindow(_monitor) { Owner = this };
        _fileVaultWindow.Closed += (_, _) => _fileVaultWindow = null;
        _fileVaultWindow.Show();
    }

    // 按记录的入口显示通用内容（各功能为独立整页，顶部显示返回 + 标题）
    private void ShowGeneralTab()
    {
        switch (_generalTab)
        {
            case GeneralTab.Export:
                ViewNoteTab.SaveNow();
                ShowPage("导出工作量");
                HideAllViews();
                ViewExportTab.Visibility = Visibility.Visible;
                ViewExportTab.Reset();
                break;
            case GeneralTab.Note:
                ShowPage("笔记");
                ShowNoteTab();
                break;
            default:
                ShowGeneralHome();
                break;
        }
    }

    // 主页：功能卡片入口
    private void ShowGeneralHome()
    {
        _generalTab = GeneralTab.Home;
        ProjectTabs.Visibility = Visibility.Collapsed;
        PageHeader.Visibility = Visibility.Collapsed;
        HideAllViews();
        ViewGeneralHome.Visibility = Visibility.Visible;
    }

    // 主页功能整页页头：隐藏项目标签、顶部显示标题
    private void ShowPage(string title)
    {
        ProjectTabs.Visibility = Visibility.Collapsed;
        PageHeader.Visibility = Visibility.Visible;
        TxtPageTitle.Text = title;
    }

    // 通用「笔记」页：重新加载列表并保持上次选中（进入前会先落盘上一篇）
    private void ShowNoteTab()
    {
        ViewNoteTab.Reload();
        HideAllViews();
        ViewNoteTab.Visibility = Visibility.Visible;
    }

    // 隐藏右侧全部视图：各 Show* 开头调用，保证切换干净（含通用主页）
    private void HideAllViews()
    {
        ViewProcessTab.Visibility = Visibility.Collapsed;
        ViewWorkloadTab.Visibility = Visibility.Collapsed;
        ViewExportTab.Visibility = Visibility.Collapsed;
        ViewShortcutTab.Visibility = Visibility.Collapsed;
        ViewNoteTab.Visibility = Visibility.Collapsed;
        ViewGeneralHome.Visibility = Visibility.Collapsed;
    }
}
