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
/// 通用已独立：只有工作量与导出；项目面板为「工序 / 工作量」两个标签页。
/// </summary>
public partial class MainWindow : Window
{
    private enum GeneralTab { Home, Export, Shortcuts, MindMap, Note, FileVault }

    private readonly string _password;
    private List<string> _projects = new();
    private List<ProcessItem> _processes = new();
    private List<PathShortcut> _shortcuts = new();
    private List<MindMap> _mindMaps = new();
    private List<NoteItem> _notes = new();
    private string? _currentProject;
    private string? _currentProcessId;
    private string? _currentMindMapId;
    private NoteItem? _currentNote;
    private bool _noteLoading;
    private bool _notesDirty;
    private GeneralTab _generalTab = GeneralTab.Home;
    private DateTime _exportDate = DateTime.Today;
    private string _shiftKey = string.Empty;
    private bool _exportToday = true;
    private bool _autoLogin;
    private bool _autoLoginUiLoading;
    private readonly InactivityMonitor _monitor = new();
    private readonly DispatcherTimer _shortcutHintTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _noteSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private static readonly Regex NumRegex = new("^[0-9]+$");

    public MainWindow()
    {
        InitializeComponent();
        _password = AuthService.SessionPassword
            ?? throw new InvalidOperationException("未登录，拒绝访问。");
        MindView.Changed += OnMindMapChanged;
        MindView.ZoomChanged += scale => TxtMindZoom.Text = $"{Math.Round(scale * 100)}%";
        MindView.Monitor = _monitor;
        _shortcutHintTimer.Tick += (_, _) =>
        {
            _shortcutHintTimer.Stop();
            RestoreShortcutHint();
        };
        _noteSaveTimer.Tick += (_, _) =>
        {
            _noteSaveTimer.Stop();
            SaveNoteNow();
        };
        LoadWindowSize();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { SaveNoteNow(); SaveWindowSize(); _monitor.Dispose(); };
    }

    // 记住用户调整过的窗口尺寸（存注册表 HKCU，与加密数据无关）
    private void LoadWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ProjectRecorder");
            if (key == null) return;
            if (key.GetValue("MainW") is int w && w >= 860 && w <= 1600) Width = w;
            if (key.GetValue("MainH") is int h && h >= 580 && h <= 1000) Height = h;
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
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\ProjectRecorder");
            key.SetValue("MainW", (int)ActualWidth, RegistryValueKind.DWord);
            key.SetValue("MainH", (int)ActualHeight, RegistryValueKind.DWord);
        }
        catch
        {
            // 存失败不影响退出
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PreviewMouseMove += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseDown += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseUp += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseWheel += (s, ev) => _monitor.NotifyActivity();
        PreviewKeyDown += (s, ev) => _monitor.NotifyActivity();
        PreviewKeyUp += (s, ev) => _monitor.NotifyActivity();
        PreviewTextInput += (s, ev) => _monitor.NotifyActivity();

        _monitor.TimedOut += () =>
        {
            // 已记住登录：取消 120 秒自动退出（重启计时，Tick 仍用于班次刷新）
            if (_autoLogin)
            {
                _monitor.Start();
                return;
            }
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        };
        _monitor.Tick += remain =>
        {
            TxtCountdown.Text = _autoLogin
                ? "已记住登录：无操作不会自动退出"
                : $"无操作 {remain} 秒后自动退出";

            // 跨班次（如 8:30 / 21:00 / 跨日）时，工作量页面自动重新统计本班数量
            string key = CurrentShiftKey();
            if (key != _shiftKey)
            {
                _shiftKey = key;
                if (ViewWorkloadTab.Visibility == Visibility.Visible)
                    ShowWorkloadTab();
            }
        };
        _shiftKey = CurrentShiftKey();

        // 同步“记住登录”状态（登录页勾选过或自动登录进来时为勾选）
        _autoLogin = AutoLoginStore.IsEnabled;
        _autoLoginUiLoading = true;
        ChkAutoLogin.IsChecked = _autoLogin;
        _autoLoginUiLoading = false;
        UpdateCountdownText();
        _monitor.Start();

        RefreshProjects();
    }

    // 状态栏“记住登录”开关：勾选保存登录信息并取消自动退出；取消勾选删除配置并恢复计时
    private void ChkAutoLogin_Changed(object sender, RoutedEventArgs e)
    {
        if (_autoLoginUiLoading) return;
        _monitor.NotifyActivity();

        if (ChkAutoLogin.IsChecked == true)
        {
            // 二次确认：避免误勾选后自动登录、取消 120 秒自动退出
            var confirm = MessageBox.Show(
                "确定要记住登录吗？\n勾选后下次启动将自动登录，并且不再进行 120 秒无操作自动退出。",
                "记住登录", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                SetAutoLoginChecked(false);
                return;
            }

            string? password = AuthService.SessionPassword;
            if (string.IsNullOrEmpty(password))
            {
                SetAutoLoginChecked(false);
                MessageBox.Show("无法获取当前登录信息，请重新登录后再试。", "记住登录",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                AutoLoginStore.Enable(password!);
                _autoLogin = true;
            }
            catch (Exception ex)
            {
                SetAutoLoginChecked(false);
                MessageBox.Show($"保存登录信息失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            AutoLoginStore.Disable();
            _autoLogin = false;
        }

        _monitor.Start();
        UpdateCountdownText();
    }

    private void SetAutoLoginChecked(bool value)
    {
        _autoLoginUiLoading = true;
        ChkAutoLogin.IsChecked = value;
        _autoLoginUiLoading = false;
    }

    private void UpdateCountdownText()
    {
        TxtCountdown.Text = _autoLogin
            ? "已记住登录：无操作不会自动退出"
            : $"无操作 {InactivityMonitor.TimeoutSeconds} 秒后自动退出";
    }

    private static string CurrentShiftKey()
    {
        var now = DateTime.Now;
        return $"{WorkloadRecord.GetShiftDate(now):yyyy-MM-dd}|{WorkloadRecord.GetShiftName(now)}";
    }

    // ---------- 项目 ----------

    private void RefreshProjects(string? keep = null)
    {
        try
        {
            _projects = DataStore.LoadProjects(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

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
        _currentProcessId = null;
        _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);

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

    // 主页按钮：功能卡片主页（导出工作量 / 快捷路径 / 思维导图 / 笔记 / 加密文件）
    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {
        OpenHome();
    }

    private void OpenHome()
    {
        _monitor.NotifyActivity();
        _currentProject = DataStore.FixedMachineProject;
        _currentProcessId = null;
        _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);
        _exportToday = true;

        BtnHome.IsChecked = true;
        LstProjects.SelectedItem = null;

        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;
        ShowGeneralHome();
    }

    // 左侧「通用」：与项目相同的上方标签栏，目前只有「工作量」一个板块（全局归属）
    private void OpenGeneralWorkload()
    {
        _monitor.NotifyActivity();
        _currentProject = DataStore.FixedMachineProject;
        _currentProcessId = null;
        _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);

        BtnHome.IsChecked = false;
        ProjectTabs.Visibility = Visibility.Visible;
        PageHeader.Visibility = Visibility.Collapsed;
        BtnTabProcess.Visibility = Visibility.Collapsed;
        BtnTabWorkload.Visibility = Visibility.Visible;
        BtnTabShortcuts.Visibility = Visibility.Collapsed;

        TxtNoProject.Visibility = Visibility.Collapsed;
        PanelProject.Visibility = Visibility.Visible;

        BtnTabWorkload.IsChecked = true;
        ShowWorkloadTab();
    }

    private void ShowNoProject()
    {
        _currentProject = null;
        _currentProcessId = null;
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
        SaveNoteNow();
        HideAllViews();
        ViewShortcutTab.Visibility = Visibility.Visible;
        RefreshShortcuts();
    }

    // ---------- 主页功能入口（卡片 → 各功能独立整页） ----------

    private void GTabExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        _generalTab = GeneralTab.Export;
        ShowGeneralTab();
    }

    private void GTabShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        _generalTab = GeneralTab.Shortcuts;
        ShowGeneralTab();
    }

    private void GTabMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        _generalTab = GeneralTab.MindMap;
        ShowGeneralTab();
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
        _generalTab = GeneralTab.FileVault;
        ShowGeneralTab();
    }

    // 按记录的入口显示通用内容（各功能为独立整页，顶部显示返回 + 标题）
    private void ShowGeneralTab()
    {
        switch (_generalTab)
        {
            case GeneralTab.Export:
                SaveNoteNow();
                ShowPage("导出工作量");
                HideAllViews();
                ViewExportTab.Visibility = Visibility.Visible;
                _exportToday = true;
                _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);
                RefreshExportDateButton();
                break;
            case GeneralTab.Shortcuts:
                ShowPage("快捷路径");
                ShowShortcutTab();
                break;
            case GeneralTab.MindMap:
                ShowPage("思维导图");
                ShowMindMapTab();
                break;
            case GeneralTab.Note:
                ShowPage("笔记");
                ShowNoteTab();
                break;
            case GeneralTab.FileVault:
                ShowPage("加密文件");
                ShowFileTab();
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

    // 隐藏右侧全部视图：各 Show* 开头调用，保证切换干净（含通用主页）
    private void HideAllViews()
    {
        ViewProcessTab.Visibility = Visibility.Collapsed;
        ViewWorkloadTab.Visibility = Visibility.Collapsed;
        ViewExportTab.Visibility = Visibility.Collapsed;
        ViewShortcutTab.Visibility = Visibility.Collapsed;
        ViewMindMapTab.Visibility = Visibility.Collapsed;
        ViewNoteTab.Visibility = Visibility.Collapsed;
        ViewFileTab.Visibility = Visibility.Collapsed;
        ViewGeneralHome.Visibility = Visibility.Collapsed;
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
        try
        {
            DataStore.SaveProjects(_projects, _password);
        }
        catch (Exception ex)
        {
            _projects.Remove(name);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

    // 空白处松开右键也不弹菜单（菜单在松开时触发，只拦按下不够）
    private void ListBlankUp(object sender, MouseButtonEventArgs e)
    {
        if (FindListBoxItem(e.OriginalSource as DependencyObject) == null)
            e.Handled = true;
    }

    private static ListBoxItem? FindListBoxItem(DependencyObject? src)
    {
        DependencyObject? d = src;
        int guard = 0;
        while (d != null && d is not ListBoxItem && guard++ < 50)
            d = SafeParent(d);
        return d as ListBoxItem;
    }

    // 防爆的取父级：Run 等非 Visual 文本元素 VisualTreeHelper 会抛异常，改走逻辑树
    private static DependencyObject? SafeParent(DependencyObject? d)
    {
        if (d == null) return null;
        try
        {
            return VisualTreeHelper.GetParent(d);
        }
        catch
        {
            try { return LogicalTreeHelper.GetParent(d); }
            catch { return null; }
        }
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
        try
        {
            DataStore.SaveProjects(_projects, _password);
            var processes = DataStore.LoadProcesses(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveProcesses(processes, _password);
            DataStore.CleanupUnusedImages(processes);
            var workload = DataStore.LoadWorkload(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveWorkload(workload, _password);
            var wprocs = DataStore.LoadWorkloadProcesses(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveWorkloadProcesses(wprocs, _password);
            var shortcuts = DataStore.LoadShortcuts(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveShortcuts(shortcuts, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        try
        {
            DataStore.SaveProjects(_projects, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshProjects(project);
            return;
        }
        RefreshProjects(project);
    }

    // ---------- 工序 ----------

    private void ShowProcessTab()
    {
        SaveNoteNow();
        HideAllViews();
        ViewProcessTab.Visibility = Visibility.Visible;
        RefreshProcessList(_currentProcessId);
    }

    private List<ProcessItem> CurrentProcesses()
    {
        try
        {
            _processes = DataStore.LoadProcesses(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _processes = new List<ProcessItem>();
        }
        return _processes.Where(x => x.ProjectName == _currentProject).ToList();
    }

    private void RefreshProcessList(string? keepProcessId = null)
    {
        var list = CurrentProcesses();
        LstProcesses.ItemsSource = null;
        LstProcesses.ItemsSource = list;
        TxtProcessEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ProcessItem? keep = keepProcessId == null
            ? null
            : list.FirstOrDefault(x => x.Id == keepProcessId);
        if (keep == null && list.Count > 0) keep = list[0];
        if (keep != null)
            LstProcesses.SelectedItem = keep;
        else
        {
            _currentProcessId = null;
            RefreshFlow();
        }
    }

    private void LstProcesses_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstProcesses.SelectedItem is ProcessItem p)
        {
            _currentProcessId = p.Id;
            RefreshFlow();
        }
    }

    private void BtnAddProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentProject)) return;

        var dlg = new AddProcessDialog(_monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var list = CurrentProcesses();
        if (list.Any(x => x.ProcessName == dlg.ProcessName))
        {
            MessageBox.Show("该工序名称已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = new ProcessItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentProject!,
            ProcessName = dlg.ProcessName,
            UpdatedTime = DateTime.Now
        };
        _processes.Add(item);
        try
        {
            DataStore.SaveProcesses(_processes, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshProcessList(item.Id);
    }

    private void LstProcesses_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void MenuDeleteProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstProcesses.SelectedItem is not ProcessItem sel)
        {
            MessageBox.Show("请先点选要删除的工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除工序「{sel.ProcessName}」吗？\n其操作流程将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        try
        {
            var all = DataStore.LoadProcesses(_password);
            all.RemoveAll(x => x.Id == sel.Id);
            DataStore.SaveProcesses(all, _password);
            DataStore.CleanupUnusedImages(all);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        _currentProcessId = null;
        RefreshProcessList();
    }

    private void MenuMoveProcessUp_Click(object sender, RoutedEventArgs e) => MoveProcess(-1);
    private void MenuMoveProcessDown_Click(object sender, RoutedEventArgs e) => MoveProcess(1);

    // 在同项目工序序列内上移/下移（全局列表中交换位置后保存）
    private void MoveProcess(int delta)
    {
        _monitor.NotifyActivity();
        if (LstProcesses.SelectedItem is not ProcessItem sel) return;
        List<ProcessItem> all;
        try
        {
            all = DataStore.LoadProcesses(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var mine = all.Where(x => x.ProjectName == _currentProject).ToList();
        int i = mine.FindIndex(x => x.Id == sel.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= mine.Count) return;
        int gi = all.IndexOf(mine[i]);
        int gj = all.IndexOf(mine[j]);
        (all[gi], all[gj]) = (all[gj], all[gi]);
        try
        {
            DataStore.SaveProcesses(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshProcessList(sel.Id);
    }

    // ---------- 步骤详情 ----------

    private ProcessItem? FindCurrentProcess()
    {
        if (string.IsNullOrEmpty(_currentProcessId)) return null;

        // 已在内存中就用内存对象：避免每次刷新/加步骤都重新解密解析整个 processes.dat（含全部图片），
        // 同时也是缩略图缓存命中的前提
        var cached = _processes.FirstOrDefault(x => x.Id == _currentProcessId);
        if (cached != null) return cached;

        try
        {
            _processes = DataStore.LoadProcesses(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        return _processes.FirstOrDefault(x => x.Id == _currentProcessId);
    }

    private void RefreshFlow()
    {
        var proc = FindCurrentProcess();
        if (proc == null)
        {
            TxtFlowTitle.Text = "操作步骤";
            LstSteps.ItemsSource = null;
            TxtStepEmpty.Text = "请从左侧选择工序";
            TxtStepEmpty.Visibility = Visibility.Visible;
            return;
        }

        TxtFlowTitle.Text = proc.ProcessName;
        LstSteps.ItemsSource = null;
        LstSteps.ItemsSource = proc.Steps;
        TxtStepEmpty.Text = "暂无步骤，点右上角“＋ 添加操作步骤”";
        TxtStepEmpty.Visibility = proc.Steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectStepById(string id)
    {
        var proc = _processes.FirstOrDefault(x => x.Id == _currentProcessId);
        var step = proc?.Steps.FirstOrDefault(x => x.Id == id);
        if (step != null) LstSteps.SelectedItem = step;
    }

    private void PersistProcesses()
    {
        var proc = _processes.FirstOrDefault(x => x.Id == _currentProcessId);
        if (proc != null)
        {
            proc.UpdatedTime = DateTime.Now;
            for (int k = 0; k < proc.Steps.Count; k++)
                proc.Steps[k].Order = k + 1;
        }
        DataStore.SaveProcesses(_processes, _password);
    }

    private void BtnAddStep_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (FindCurrentProcess() == null) return;

        var dlg = new StepDialog(_monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var target = _processes.First(x => x.Id == _currentProcessId);
        var step = new FlowStep
        {
            Id = Guid.NewGuid().ToString(),
            Text = dlg.StepText,
            Images = new List<StepImage>(dlg.Images)
        };
        target.Steps.Add(step);
        try
        {
            PersistProcesses();
        }
        catch (Exception ex)
        {
            target.Steps.Remove(step);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshFlow();
        SelectStepById(step.Id);
    }

    // 卡片文字只读可选（可复制）；右键点在文字上走复制菜单，点卡片其他位置先选中（菜单里排序/删除）
    private void LstSteps_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        for (DependencyObject? d = src; d != null; d = SafeParent(d))
        {
            if (d is TextBox) return;
        }

        var item = FindListBoxItem(src);
        if (item != null) item.IsSelected = true;
    }

    private void MenuStepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void MenuStepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int delta)
    {
        _monitor.NotifyActivity();
        if (LstSteps.SelectedItem is not FlowStep step) return;
        var proc = FindCurrentProcess();
        if (proc == null) return;

        int i = proc.Steps.FindIndex(x => x.Id == step.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= proc.Steps.Count) return;
        (proc.Steps[i], proc.Steps[j]) = (proc.Steps[j], proc.Steps[i]);
        try
        {
            PersistProcesses();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshFlow();
            return;
        }

        RefreshFlow();
        SelectStepById(step.Id);
    }

    private void MenuStepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LstSteps.SelectedItem is not FlowStep step)
        {
            MessageBox.Show("请先右键点选要删除的步骤。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DeleteStep(step);
    }

    // 点击图片放大查看（默认原图 1:1；同一步骤多图可翻页）
    private void StepImage_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is not StepImage img) return;
        if (img.Data is not { Length: > 0 } && string.IsNullOrEmpty(img.Id)) return;

        FlowStep? step = null;
        if (FindListBoxItemContainer(sender) is ListBoxItem item)
            step = item.DataContext as FlowStep;

        var proc = FindCurrentProcess();
        string title = proc != null && step != null ? $"{proc.ProcessName} 第 {step.Order} 步" : "查看图片";
        var list = step?.Images;
        if (list != null && list.Count > 0)
        {
            int idx = list.FindIndex(x => x.Id == img.Id);
            if (idx < 0) idx = 0;
            new ImageViewerDialog(list, idx, title, _monitor) { Owner = this }.ShowDialog();
        }
        else
        {
            byte[]? data = DataStore.GetImageBytes(img);
            if (data == null || data.Length == 0) return;
            new ImageViewerDialog(data, title, _monitor) { Owner = this }.ShowDialog();
        }
        _monitor.NotifyActivity();
    }

    private void DeleteStep(FlowStep step)
    {
        _monitor.NotifyActivity();
        var proc = FindCurrentProcess();
        if (proc == null) return;
        if (!proc.Steps.Any(x => x.Id == step.Id)) return;

        var r = MessageBox.Show($"确定删除第 {step.Order} 步吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        var target = _processes.First(x => x.Id == _currentProcessId);
        target.Steps.RemoveAll(x => x.Id == step.Id);
        try
        {
            PersistProcesses();
            DataStore.CleanupUnusedImages(_processes);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshFlow();
    }

    // ---------- 工作量 ----------

    // 通用「导出」入口：默认导出本班次；选日期后导出该日期全天合计（xlsx，冻结标题行）
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();

        DateTime day;
        string? shift = null;
        string label;
        string fileName;
        if (_exportToday)
        {
            var now = DateTime.Now;
            day = WorkloadRecord.GetShiftDate(now);
            shift = WorkloadRecord.GetShiftName(now);
            label = $"{day:yyyy-MM-dd} {shift}";
            fileName = $"工作量_{day:yyyy-MM-dd}_{shift}.xlsx";
        }
        else
        {
            day = _exportDate.Date;
            label = $"{day:yyyy-MM-dd}";
            fileName = $"工作量_{day:yyyy-MM-dd}.xlsx";
        }

        List<WorkloadRecord> list;
        try
        {
            list = DataStore.LoadWorkload(_password)
                .Where(x => x.WorkDate.Date == day.Date && (shift == null || x.Shift == shift))
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (list.Count == 0)
        {
            MessageBox.Show($"{label} 暂无工作量记录。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fileDlg = new SaveFileDialog
        {
            Title = "导出工作量",
            Filter = "Excel 文件|*.xlsx",
            FileName = fileName
        };
        if (fileDlg.ShowDialog(this) != true) return;

        try
        {
            int rowCount = ExcelExporter.ExportWorkload(fileDlg.FileName, day, list, shift);
            MessageBox.Show($"导出成功，共 {rowCount} 项合计：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 工作量页：只显示班次日期与白/夜班
    private void ShowWorkloadTab()
    {
        SaveNoteNow();
        HideAllViews();
        ViewWorkloadTab.Visibility = Visibility.Visible;

        var now = DateTime.Now;
        TxtWorkShift.Text = $"{WorkloadRecord.GetShiftDate(now):yyyy-MM-dd} {WorkloadRecord.GetShiftName(now)}";
        _shiftKey = CurrentShiftKey();
        RefreshWorkCards();
    }

    // 导出页：默认“今日”（本班次日期），上限封顶到班次今日，不能选未来
    private void RefreshExportDateButton()
    {
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        ExpPickDate.Content = _exportToday ? "今日" : _exportDate.ToString("yyyy-MM-dd");
        ExpNextDay.IsEnabled = !_exportToday;
    }

    private void ExpPrevDay_Click(object sender, RoutedEventArgs e) => ChangeExportDate(-1);
    private void ExpNextDay_Click(object sender, RoutedEventArgs e) => ChangeExportDate(1);

    private void ChangeExportDate(int days)
    {
        _monitor.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        DateTime baseDay = _exportToday ? today : _exportDate.Date;
        if (baseDay > today) baseDay = today;
        DateTime next = baseDay.AddDays(days);
        if (next >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        else
        {
            _exportToday = false;
            _exportDate = next;
        }
        RefreshExportDateButton();
    }

    private void ExpPickDate_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date > today.Date)
        {
            _exportToday = true;
            _exportDate = today;
            RefreshExportDateButton();
        }
        try
        {
            ExpCal.DisplayDateStart = null;
            ExpCal.DisplayDateEnd = today;
            ExpCal.BlackoutDates.Clear();
            if (today.Date < new DateTime(9999, 12, 31))
                ExpCal.BlackoutDates.Add(new CalendarDateRange(today.Date.AddDays(1), new DateTime(9999, 12, 31)));
        }
        catch
        {
            // 日历限制设置失败不影响打开
        }
        ExpCal.SelectedDate = _exportToday ? today : _exportDate;
        ExpCal.DisplayDate = ExpCal.SelectedDate ?? today;
        if (ExpCal.DisplayDate > today) ExpCal.DisplayDate = today;
        ExpPopCalendar.IsOpen = true;
    }

    private void ExpCal_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (ExpCal.SelectedDate.HasValue)
        {
            DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
            DateTime picked = ExpCal.SelectedDate.Value.Date;
            if (picked >= today.Date)
            {
                _exportToday = true;
                _exportDate = today;
            }
            else
            {
                _exportDate = picked;
                _exportToday = false;
            }
            RefreshExportDateButton();
        }
        ExpPopCalendar.IsOpen = false;
    }

    private sealed class WorkCard
    {
        public WorkloadProcess Process { get; set; } = null!;
        /// <summary>本班次数量（换班自动从 0 重新统计）。</summary>
        public int ShiftQty { get; set; }
    }

    private void RefreshWorkCards()
    {
        List<WorkloadProcess> procs;
        List<WorkloadRecord> records;
        try
        {
            procs = DataStore.LoadWorkloadProcesses(_password)
                .Where(x => x.ProjectName == _currentProject)
                .ToList();
            records = DataStore.LoadWorkload(_password)
                .Where(x => x.ProjectName == _currentProject)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 只统计当前班次：班次日期 + 白/夜班都一致；换班次后卡片自动归零重新统计
        DateTime shiftDate = WorkloadRecord.GetShiftDate(DateTime.Now);
        string shiftName = WorkloadRecord.GetShiftName(DateTime.Now);

        var cards = procs
            .OrderBy(x => x.Order)
            .ThenBy(x => x.CreatedTime)
            .Select(p => new WorkCard
            {
                Process = p,
                ShiftQty = Math.Max(0, records
                    .Where(r => r.ProcessName == p.ProcessName
                                && r.WorkDate.Date == shiftDate
                                && r.Shift == shiftName)
                    .Sum(r => r.Quantity))
            }).ToList();

        LstWorkProcess.ItemsSource = null;
        LstWorkProcess.ItemsSource = cards;
        TxtWProcessEmpty.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnAddWProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentProject)) return;

        var dlg = new AddProcessDialog(_monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        List<WorkloadProcess> procs;
        try
        {
            procs = DataStore.LoadWorkloadProcesses(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (procs.Any(x => x.ProjectName == _currentProject && x.ProcessName == dlg.ProcessName))
        {
            MessageBox.Show("该工序已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        procs.Add(new WorkloadProcess
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentProject!,
            ProcessName = dlg.ProcessName,
            Order = procs.Where(x => x.ProjectName == _currentProject).Select(x => x.Order).DefaultIfEmpty(0).Max() + 1,
            CreatedTime = DateTime.Now
        });
        try
        {
            DataStore.SaveWorkloadProcesses(procs, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshWorkCards();
    }

    private static TextBox? FindQtyBox(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        int guard = 0;
        while (queue.Count > 0 && guard++ < 200)
        {
            var d = queue.Dequeue();
            if (d is TextBox tb && tb.Name == "QtyBox") return tb;
            int n;
            try { n = VisualTreeHelper.GetChildrenCount(d); }
            catch { continue; }
            for (int i = 0; i < n; i++)
            {
                try { queue.Enqueue(VisualTreeHelper.GetChild(d, i)); }
                catch { /* 忽略非可视化子级 */ }
            }
        }
        return null;
    }

    private static DependencyObject? FindListBoxItemContainer(object sender)
    {
        DependencyObject? d = sender as DependencyObject;
        int guard = 0;
        while (d != null && d is not ListBoxItem && guard++ < 30)
            d = VisualTreeHelper.GetParent(d);
        return d;
    }

    private int ReadCardQty(object sender, out TextBox? box)
    {
        box = null;
        var container = FindListBoxItemContainer(sender);
        if (container != null) box = FindQtyBox(container);
        if (box == null) return 1;
        if (!int.TryParse(box.Text.Trim(), out int v)) return 1;
        return v;
    }

    // 步进不经过 0：1 减一 = -1，-1 加一 = 1
    private void AdjustCardQty(object sender, int delta)
    {
        _monitor.NotifyActivity();
        int v = ReadCardQty(sender, out var box);
        v += delta;
        if (v == 0) v = delta > 0 ? 1 : -1;
        if (v > 999999) v = 999999;
        if (v < -999999) v = -999999;
        if (box != null) box.Text = v.ToString();
    }

    private void BtnWCardMinus_Click(object sender, RoutedEventArgs e) => AdjustCardQty(sender, -1);
    private void BtnWCardPlus_Click(object sender, RoutedEventArgs e) => AdjustCardQty(sender, 1);

    // 只允许数字，以及最前面的一个负号
    private void TxtQty_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (NumRegex.IsMatch(e.Text))
        {
            e.Handled = false;
            return;
        }
        if (e.Text == "-" && sender is TextBox tb
            && tb.SelectionLength == 0 && tb.CaretIndex == 0 && !tb.Text.StartsWith("-"))
        {
            e.Handled = false;
            return;
        }
        e.Handled = true;
    }

    private void BtnWCardSave_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentProject))
            return;
        if ((sender as FrameworkElement)?.DataContext is not WorkCard card)
            return;

        int qty = ReadCardQty(sender, out var box);
        if (box == null) return;
        // 数量不能为 0（不弹提示，直接重置）
        if (!int.TryParse(box.Text.Trim(), out qty) || qty == 0)
        {
            box.Text = "1";
            return;
        }

        // 负数冲减，本班数量最低为 0：超过本班数量就直接按本班数量冲减到 0
        if (qty < 0 && card.ShiftQty + qty < 0)
        {
            qty = -card.ShiftQty;
            if (qty == 0) return;
        }

        var rec = new WorkloadRecord
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentProject!,
            ProcessName = card.Process.ProcessName,
            WorkDate = WorkloadRecord.GetShiftDate(DateTime.Now),
            Shift = WorkloadRecord.GetShiftName(DateTime.Now),
            Quantity = qty,
            Notes = string.Empty,
            CreatedTime = DateTime.Now
        };

        try
        {
            var all = DataStore.LoadWorkload(_password);
            all.Add(rec);
            DataStore.SaveWorkload(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (box != null) box.Text = "1";
        RefreshWorkCards();
    }

    private void LstWorkProcess_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            // 空白处右键：不选中也不弹菜单
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void MoveWProcess(object sender, int delta)
    {
        _monitor.NotifyActivity();
        var card = LstWorkProcess.SelectedItem as WorkCard;
        if (card == null)
        {
            if ((sender as FrameworkElement)?.DataContext is WorkCard ctx)
                card = ctx;
        }
        if (card == null) return;

        List<WorkloadProcess> mine;
        try
        {
            mine = DataStore.LoadWorkloadProcesses(_password)
                .Where(x => x.ProjectName == _currentProject)
                .OrderBy(x => x.Order)
                .ThenBy(x => x.CreatedTime)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        int i = mine.FindIndex(x => x.Id == card.Process.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= mine.Count) return;

        try
        {
            var all = DataStore.LoadWorkloadProcesses(_password);
            var a = all.First(x => x.Id == mine[i].Id);
            var b = all.First(x => x.Id == mine[j].Id);
            (a.Order, b.Order) = (b.Order, a.Order);
            DataStore.SaveWorkloadProcesses(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshWorkCards();
        var moved = (LstWorkProcess.ItemsSource as List<WorkCard>)
            ?.FirstOrDefault(x => x.Process.Id == card.Process.Id);
        if (moved != null) LstWorkProcess.SelectedItem = moved;
    }

    private void MenuWProcessUp_Click(object sender, RoutedEventArgs e) => MoveWProcess(sender, -1);
    private void MenuWProcessDown_Click(object sender, RoutedEventArgs e) => MoveWProcess(sender, 1);

    private void MenuDeleteWProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if ((LstWorkProcess.SelectedItem as WorkCard)?.Process is not WorkloadProcess sel)
        {
            MessageBox.Show("请先右键点选要删除的工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除工序「{sel.ProcessName}」吗？\n其工作量记录将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        try
        {
            var procs = DataStore.LoadWorkloadProcesses(_password);
            procs.RemoveAll(x => x.Id == sel.Id);
            DataStore.SaveWorkloadProcesses(procs, _password);
            var all = DataStore.LoadWorkload(_password);
            all.RemoveAll(x => x.ProjectName == _currentProject && x.ProcessName == sel.ProcessName);
            DataStore.SaveWorkload(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshWorkCards();
    }

    // ---------- 快捷路径（通用 / 项目各自一份） ----------

    private void RefreshShortcuts()
    {
        List<PathShortcut> all;
        try
        {
            all = DataStore.LoadShortcuts(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string owner = _currentProject ?? DataStore.FixedMachineProject;
        bool isGeneral = owner == DataStore.FixedMachineProject;
        _shortcuts = all.Where(x =>
        {
            // 旧数据没有 ProjectName，视为通用模块
            string p = string.IsNullOrWhiteSpace(x.ProjectName) ? DataStore.FixedMachineProject : x.ProjectName;
            return p == owner;
        }).ToList();

        var folders = _shortcuts.Where(x => !x.IsUrl).ToList();
        // 网址快捷路径只在通用模块显示（项目内只显示文件夹）
        var urls = isGeneral
            ? _shortcuts.Where(x => x.IsUrl).ToList()
            : new List<PathShortcut>();

        UrlShortcutPanel.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        ColShortcutGap.Width = isGeneral ? new GridLength(14) : new GridLength(0);
        ColShortcutUrl.Width = isGeneral ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TxtShortcutHint.Text = ShortcutHintDefault();

        LstFolderShortcuts.ItemsSource = null;
        LstFolderShortcuts.ItemsSource = folders;
        TxtFolderShortcutEmpty.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LstUrlShortcuts.ItemsSource = null;
        LstUrlShortcuts.ItemsSource = urls;
        TxtUrlShortcutEmpty.Visibility = urls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ShortcutHintDefault()
        => _currentProject == DataStore.FixedMachineProject
            ? "文件夹：单击用资源管理器打开；网址：单击复制到剪贴板（不打开浏览器）"
            : "文件夹：单击用资源管理器打开";

    // 统一添加入口：弹窗内选名称 + 类型（项目内只能选文件夹）
    private void BtnAddShortcut_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentProject)) return;

        bool allowUrl = _currentProject == DataStore.FixedMachineProject;
        var dlg = new PathShortcutDialog(_monitor, allowUrl) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (_shortcuts.Any(x => x.Name == dlg.ShortcutName && x.FolderPath == dlg.ShortcutPath))
        {
            MessageBox.Show("同名同地址的快捷方式已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var all = DataStore.LoadShortcuts(_password);
            all.Add(new PathShortcut
            {
                Id = Guid.NewGuid().ToString(),
                ProjectName = _currentProject!,
                Kind = dlg.ShortcutKind,
                Name = dlg.ShortcutName,
                FolderPath = dlg.ShortcutPath,
                CreatedTime = DateTime.Now
            });
            DataStore.SaveShortcuts(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshShortcuts();
    }

    // 文件夹卡片：单击用资源管理器打开
    private void ShortcutFolder_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor.NotifyActivity();
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

    // 网址卡片：单击复制到剪贴板（不打开浏览器）
    private void ShortcutUrl_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is PathShortcut sc) CopyShortcutUrl(sc);
    }

    private void MenuCopyUrlShortcut_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstUrlShortcuts.SelectedItem is PathShortcut sc) CopyShortcutUrl(sc);
    }

    private void CopyShortcutUrl(PathShortcut shortcut)
    {
        string url = (shortcut.FolderPath ?? string.Empty).Trim();
        if (url.Length == 0) return;
        if (CopyToClipboard(url))
        {
            ShowShortcutHint($"已复制网址：{url}");
            return;
        }

        // 失败不重试：直接打开手动复制弹窗
        new ManualCopyDialog(url, _monitor) { Owner = this }.ShowDialog();
        _monitor.NotifyActivity();
    }

    // 复制到剪贴板（单次尝试）：剪贴板 API 在被占用时会阻塞不返回，
    // 所以放到独立 STA 线程做、带 700ms 超时，保证 UI 不会卡死
    internal static bool CopyToClipboard(string text)
    {
        var finished = new ManualResetEventSlim(false);
        bool ok = false;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                ok = true;
            }
            catch
            {
                // 失败（如剪贴板被锁定）直接返回，由调用方打开手动复制弹窗
            }
            finally
            {
                finished.Set();
            }
        });
        try
        {
            thread.SetApartmentState(ApartmentState.STA); // 剪贴板 API 要求 STA 线程
            thread.IsBackground = true;
            thread.Start();
        }
        catch
        {
            return false;
        }
        return finished.Wait(700) && ok;
    }

    // 复制反馈：标题行临时显示 2 秒
    private void ShowShortcutHint(string text)
    {
        TxtShortcutHint.Text = text;
        TxtShortcutHint.Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        _shortcutHintTimer.Stop();
        _shortcutHintTimer.Start();
    }

    private void RestoreShortcutHint()
    {
        TxtShortcutHint.Text = ShortcutHintDefault();
        TxtShortcutHint.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    }

    private void LstShortcuts_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void MenuDeleteFolderShortcut_Click(object sender, RoutedEventArgs e)
        => DeleteShortcut(LstFolderShortcuts.SelectedItem as PathShortcut);

    private void MenuDeleteUrlShortcut_Click(object sender, RoutedEventArgs e)
        => DeleteShortcut(LstUrlShortcuts.SelectedItem as PathShortcut);

    private void DeleteShortcut(PathShortcut? sel)
    {
        _monitor.NotifyActivity();
        if (sel == null)
        {
            MessageBox.Show("请先右键点选要删除的快捷路径。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除快捷路径「{sel.Name}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        try
        {
            var all = DataStore.LoadShortcuts(_password);
            all.RemoveAll(x => x.Id == sel.Id);
            DataStore.SaveShortcuts(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshShortcuts();
    }

    // ---------- 笔记（通用模块独立功能） ----------

    private void ShowNoteTab()
    {
        SaveNoteNow();
        HideAllViews();
        ViewNoteTab.Visibility = Visibility.Visible;

        string? keepId = _currentNote?.Id;
        try
        {
            _notes = DataStore.LoadNotes(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _notes = new List<NoteItem>();
        }
        RefreshNoteList(keepId);
    }

    private void RefreshNoteList(string? keepId = null)
    {
        LstNotes.ItemsSource = null;
        LstNotes.ItemsSource = _notes;
        TxtNoteEmpty.Visibility = _notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        NoteItem? keep = keepId == null ? null : _notes.FirstOrDefault(x => x.Id == keepId);
        if (keep == null && _notes.Count > 0) keep = _notes[0];
        if (keep != null)
        {
            LstNotes.SelectedItem = keep;
        }
        else
        {
            _currentNote = null;
            ShowNoNoteEditor();
        }
    }

    private void LstNotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_noteLoading) return;
        if (LstNotes.SelectedItem is not NoteItem note) return;
        if (ReferenceEquals(note, _currentNote)) return;

        SaveNoteNow();
        LoadNoteIntoEditor(note);
    }

    private void LoadNoteIntoEditor(NoteItem note)
    {
        _currentNote = note;
        _noteLoading = true;
        try
        {
            TxtNoteTitle.Text = note.Title ?? string.Empty;
            TxtNoteBody.Text = note.Content ?? string.Empty;
        }
        finally
        {
            _noteLoading = false;
        }

        TxtNoteTitle.IsEnabled = true;
        TxtNoteBody.IsEnabled = true;
        TxtNoteNoSel.Visibility = Visibility.Collapsed;
        TxtNoteStatus.Text = string.Empty;
    }

    private void ShowNoNoteEditor()
    {
        _noteLoading = true;
        try
        {
            TxtNoteTitle.Text = string.Empty;
            TxtNoteBody.Text = string.Empty;
        }
        finally
        {
            _noteLoading = false;
        }

        TxtNoteTitle.IsEnabled = false;
        TxtNoteBody.IsEnabled = false;
        TxtNoteNoSel.Visibility = Visibility.Visible;
        TxtNoteStatus.Text = string.Empty;
    }

    // 输入即更新内存模型（列表卡片标题/时间实时刷新），磁盘保存用 700ms 防抖
    private void NoteText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_noteLoading || _currentNote == null) return;

        _currentNote.Title = TxtNoteTitle.Text;
        _currentNote.Content = TxtNoteBody.Text;
        _notesDirty = true;
        TxtNoteStatus.Text = "编辑中…";
        _noteSaveTimer.Stop();
        _noteSaveTimer.Start();
    }

    // 立即保存未落盘的笔记改动（切页/切笔记/退出时调用）
    private void SaveNoteNow()
    {
        if (!_notesDirty || _currentNote == null) return;

        _noteSaveTimer.Stop();
        _currentNote.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveNotes(_notes, _password);
            _notesDirty = false;
            TxtNoteStatus.Text = $"已自动保存 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddNote_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        SaveNoteNow();

        var note = new NoteItem { Title = "新笔记", Content = string.Empty };
        _notes.Insert(0, note);
        try
        {
            DataStore.SaveNotes(_notes, _password);
        }
        catch (Exception ex)
        {
            _notes.Remove(note);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshNoteList(note.Id);
        TxtNoteTitle.Focus();
        TxtNoteTitle.SelectAll();
    }

    private void LstNotes_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void MenuDeleteNote_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstNotes.SelectedItem is not NoteItem note)
        {
            MessageBox.Show("请先点选要删除的笔记。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除笔记「{note.DisplayTitle}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        _noteSaveTimer.Stop();
        _notesDirty = false;
        int index = _notes.IndexOf(note);
        _notes.Remove(note);
        try
        {
            DataStore.SaveNotes(_notes, _password);
        }
        catch (Exception ex)
        {
            _notes.Insert(Math.Min(index, _notes.Count), note);
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _currentNote = null;
        RefreshNoteList();
    }

    // ---------- 加密文件（通用模块独立功能） ----------

    private const long MaxImportFileSize = 200L * 1024 * 1024;

    private void ShowFileTab()
    {
        HideAllViews();
        ViewFileTab.Visibility = Visibility.Visible;
        RefreshEncryptedFiles();
    }

    private void RefreshEncryptedFiles()
    {
        List<EncryptedFile> files;
        try
        {
            files = DataStore.LoadEncryptedFiles(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            files = new List<EncryptedFile>();
        }

        LstEncryptedFiles.ItemsSource = null;
        LstEncryptedFiles.ItemsSource = files;
        TxtFileEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        long total = files.Sum(x => x.Size);
        TxtFileVaultInfo.Text = files.Count == 0
            ? "加密文件：导入后 AES-256 加密存本地，绝不明文落盘；导出的是解密副本，请妥善保管"
            : $"共 {files.Count} 个加密文件，合计 {EncryptedFile.FormatSize(total)}；导出的是解密副本，请妥善保管";
    }

    private void BtnImportFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        var dlg = new OpenFileDialog
        {
            Title = "选择要加密保存的文件",
            Multiselect = true,
            Filter = "所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        List<EncryptedFile> files;
        try
        {
            files = DataStore.LoadEncryptedFiles(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var added = new List<EncryptedFile>();
        var errors = new List<string>();
        foreach (string path in dlg.FileNames)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxImportFileSize)
                {
                    errors.Add($"{info.Name}：超过 {EncryptedFile.FormatSize(MaxImportFileSize)}，未导入");
                    continue;
                }

                byte[] data = File.ReadAllBytes(path);
                var item = new EncryptedFile
                {
                    Name = info.Name,
                    OriginalName = info.Name,
                    Size = info.Length
                };
                DataStore.SaveEncryptedFileContent(item.Id, data, _password);
                added.Add(item);
                files.Add(item);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}：{ex.Message}");
            }
        }

        if (added.Count > 0)
        {
            try
            {
                DataStore.SaveEncryptedFiles(files, _password);
            }
            catch (Exception ex)
            {
                // 元数据没存上：删掉刚写入的加密文件，避免留下没有记录的垃圾
                foreach (var item in added)
                {
                    files.Remove(item);
                    DataStore.DeleteEncryptedFileContent(item.Id);
                }
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        RefreshEncryptedFiles();
        if (errors.Count > 0)
        {
            MessageBox.Show("以下文件未导入：\n" + string.Join("\n", errors), "导入",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (added.Count > 0)
        {
            MessageBox.Show($"已加密保存 {added.Count} 个文件。", "导入",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void LstEncryptedFiles_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private List<EncryptedFile> CurrentEncryptedFiles()
        => LstEncryptedFiles.ItemsSource as List<EncryptedFile> ?? new List<EncryptedFile>();

    // 导出解密副本（明文）：仅这一个出口，请自行妥善保管
    private void MenuExportEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要导出的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string defaultName = string.IsNullOrWhiteSpace(item.OriginalName) ? item.DisplayName : item.OriginalName;
        var dlg = new SaveFileDialog
        {
            Title = "导出解密副本（明文，请妥善保管）",
            FileName = defaultName,
            Filter = "所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            byte[] data = DataStore.LoadEncryptedFileContent(item.Id, _password);
            File.WriteAllBytes(dlg.FileName, data);
            MessageBox.Show($"已导出解密副本（明文文件）：\n{dlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuRenameEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要重命名的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new NameInputDialog("重命名加密文件", "显示名称 *", item.Name, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Value == item.Name) return;

        string oldName = item.Name;
        item.Name = dlg.Value;
        try
        {
            DataStore.SaveEncryptedFiles(CurrentEncryptedFiles(), _password);
        }
        catch (Exception ex)
        {
            item.Name = oldName;
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshEncryptedFiles();
    }

    private void MenuDeleteEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要删除的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除加密文件「{item.DisplayName}」吗？\n删除后无法恢复（建议先导出解密副本备份）。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        var files = CurrentEncryptedFiles();
        files.Remove(item);
        try
        {
            DataStore.SaveEncryptedFiles(files, _password);
        }
        catch (Exception ex)
        {
            files.Add(item);
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DataStore.DeleteEncryptedFileContent(item.Id);
        RefreshEncryptedFiles();
    }

    // 仅 .md/.sh/.py 支持在新窗口在线编辑，右键菜单按选中项启用/置灰
    private void EncryptedFilesMenu_Opened(object sender, RoutedEventArgs e)
    {
        MenuEditEncryptedFile.IsEnabled = LstEncryptedFiles.SelectedItem is EncryptedFile item && item.CanEdit;
    }

    private void MenuEditEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要编辑的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenEncryptedFileEditor(item);
    }

    private void LstEncryptedFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstEncryptedFiles.SelectedItem is EncryptedFile item && item.CanEdit)
        {
            _monitor.NotifyActivity();
            OpenEncryptedFileEditor(item);
        }
    }

    private void OpenEncryptedFileEditor(EncryptedFile item)
    {
        if (!item.CanEdit)
        {
            MessageBox.Show($"「{item.DisplayName}」不是 .md / .sh / .py 文件，暂不支持在线编辑。\n" +
                            "可先导出解密副本，用其它工具编辑后再导入。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        byte[] data;
        try
        {
            data = DataStore.LoadEncryptedFileContent(item.Id, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var editor = new CodeEditorWindow(item, data, _password, _monitor, OnEditorSaved) { Owner = this };
            editor.ShowDialog();
        }
        catch (Exception ex)
        {
            // 编辑器加载失败只弹提示、不退出主程序；详情记入 error.log（exe 同级目录）
            App.ReportFatal("在线编辑", ex);
            return;
        }
        RefreshEncryptedFiles();
    }

    // 编辑器保存内容后回调：刷新文件大小等元数据（内容本身已在编辑器里加密写回）
    private void OnEditorSaved(EncryptedFile item)
    {
        try
        {
            DataStore.SaveEncryptedFiles(CurrentEncryptedFiles(), _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"文件内容已加密保存，但文件列表信息保存失败：{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 思维导图（通用模块独立功能） ----------

    private void ShowMindMapTab()
    {
        SaveNoteNow();
        HideAllViews();
        ViewMindMapTab.Visibility = Visibility.Visible;

        try
        {
            _mindMaps = DataStore.LoadMindMaps(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _mindMaps = new List<MindMap>();
        }
        RefreshMindMapList(_currentMindMapId);
    }

    private void RefreshMindMapList(string? keepId = null)
    {
        LstMindMaps.ItemsSource = null;
        LstMindMaps.ItemsSource = _mindMaps;
        TxtMindPopEmpty.Visibility = _mindMaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MindEmptyPanel.Visibility = _mindMaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        MindMap? keep = keepId == null ? null : _mindMaps.FirstOrDefault(x => x.Id == keepId);
        if (keep == null && _mindMaps.Count > 0) keep = _mindMaps[0];
        if (keep == null)
        {
            _currentMindMapId = null;
            TxtMindMapTitle.Text = "思维导图";
            MindCanvasBorder.Visibility = Visibility.Collapsed;
            MindView.SetMap(null);
            return;
        }

        _currentMindMapId = keep.Id;
        LstMindMaps.SelectedItem = keep;
    }

    private void LstMindMaps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstMindMaps.SelectedItem is not MindMap map) return;
        _currentMindMapId = map.Id;
        TxtMindMapTitle.Text = map.Name;

        // 打开下拉时刷新列表会重设选中项：同一张图不重复 SetMap（否则每次弹下拉都会重置缩放）
        if (ReferenceEquals(MindView.Map, map))
        {
            MindPop.IsOpen = false;
            return;
        }

        MindEmptyPanel.Visibility = Visibility.Collapsed;
        MindCanvasBorder.Visibility = Visibility.Visible;
        MindView.SetMap(map);
        MindPop.IsOpen = false;
    }

    // 节点文字/结构变化：更新导图时间并整体加密保存
    private void OnMindMapChanged()
    {
        var map = _mindMaps.FirstOrDefault(x => x.Id == _currentMindMapId);
        if (map == null) return;
        map.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveMindMaps(_mindMaps, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 顶部导图名按钮：空列表直接新建，否则弹下拉（切换/新建/重命名/删除）
    private void BtnMindMapSwitch_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (_mindMaps.Count == 0)
        {
            AddMindMap();
            return;
        }
        if (MindPop.IsOpen) return;
        RefreshMindMapList(_currentMindMapId);
        MindPop.IsOpen = true;
    }

    private void BtnAddMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        AddMindMap();
    }

    private void AddMindMap()
    {
        var dlg = new NameInputDialog("新建思维导图", "导图名称 *", string.Empty, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (_mindMaps.Any(x => x.Name == dlg.Value))
        {
            MessageBox.Show("该导图名称已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var map = new MindMap
        {
            Name = dlg.Value,
            Root = new MindNode { Title = "中心主题" }
        };
        _mindMaps.Add(map);
        try
        {
            DataStore.SaveMindMaps(_mindMaps, _password);
        }
        catch (Exception ex)
        {
            _mindMaps.Remove(map);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshMindMapList(map.Id);
        MindPop.IsOpen = false;
        MindView.BeginEditRoot();
    }

    private void MenuRenameMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstMindMaps.SelectedItem is not MindMap map)
        {
            MessageBox.Show("请先点选要重命名的导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new NameInputDialog("重命名导图", "导图名称 *", map.Name, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Value == map.Name) return;
        if (_mindMaps.Any(x => x.Id != map.Id && x.Name == dlg.Value))
        {
            MessageBox.Show("该导图名称已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string oldName = map.Name;
        map.Name = dlg.Value;
        map.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveMindMaps(_mindMaps, _password);
        }
        catch (Exception ex)
        {
            map.Name = oldName;
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        TxtMindMapTitle.Text = map.Name;
        RefreshMindMapList(map.Id);
    }

    private void MenuDeleteMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (LstMindMaps.SelectedItem is not MindMap map)
        {
            MessageBox.Show("请先点选要删除的导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除导图「{map.Name}」吗？\n其全部节点将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        int index = _mindMaps.IndexOf(map);
        _mindMaps.Remove(map);
        try
        {
            DataStore.SaveMindMaps(_mindMaps, _password);
        }
        catch (Exception ex)
        {
            _mindMaps.Insert(Math.Min(index, _mindMaps.Count), map);
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _currentMindMapId = null;
        RefreshMindMapList();
    }

    private void BtnMindZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        MindView.ZoomIn();
    }

    private void BtnMindZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        MindView.ZoomOut();
    }

    private void BtnMindFit_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        MindView.Fit();
    }

    // 操作教程：单独按钮唤起说明弹窗
    private void BtnMindMapHelp_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        new MindMapHelpDialog(_monitor) { Owner = this }.ShowDialog();
        _monitor.NotifyActivity();
    }

    // 导出：按按钮弹出菜单选择图片（PNG）或文本大纲（TXT）
    private void BtnMindExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (_currentMindMap() == null || MindView.Map == null)
        {
            MessageBox.Show("请先选择或新建一张思维导图。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        var png = new MenuItem { Header = "导出为图片（PNG）" };
        png.Click += (_, _) => ExportMindMapPng();
        menu.Items.Add(png);
        var txt = new MenuItem { Header = "导出为文本大纲（TXT，适合给 AI）" };
        txt.Click += (_, _) => ExportMindMapText();
        menu.Items.Add(txt);

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private MindMap? _currentMindMap()
        => _mindMaps.FirstOrDefault(x => x.Id == _currentMindMapId);

    // 图片导出：整张导图渲染成 PNG（白底、2 倍分辨率，不含选中高亮）
    private void ExportMindMapPng()
    {
        var map = _currentMindMap();
        if (map == null) return;

        var fileDlg = new SaveFileDialog
        {
            Title = "导出为图片",
            Filter = "PNG 图片|*.png",
            FileName = SafeFileName(map.Name) + ".png"
        };
        if (fileDlg.ShowDialog(this) != true) return;

        try
        {
            MindView.ExportPng(fileDlg.FileName);
            MessageBox.Show($"导出成功：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 文本导出：Markdown 风格缩进大纲（标题 + 内容），方便直接给 AI 提需求
    private void ExportMindMapText()
    {
        var map = _currentMindMap();
        if (map == null) return;

        var fileDlg = new SaveFileDialog
        {
            Title = "导出为文本大纲",
            Filter = "文本文件|*.txt",
            FileName = SafeFileName(map.Name) + ".txt"
        };
        if (fileDlg.ShowDialog(this) != true) return;

        try
        {
            int count = MindMapOutlineExporter.ExportTxt(fileDlg.FileName, map);
            MessageBox.Show($"导出成功，共 {count} 个节点：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeFileName(string name)
    {
        string result = (name ?? string.Empty).Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');
        return result.Length == 0 ? "思维导图" : result;
    }
}
