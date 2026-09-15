using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 主窗口：主界面 / 项目列表 / 工序列表 / 工序详情，4 视图同窗切换。
/// 工序归属项目独立；操作流程为逐条步骤；无完成次数字段。
/// </summary>
public partial class MainWindow : Window
{
    private readonly string _password;
    private List<string> _projects = new();
    private List<ProcessItem> _processes = new();
    private List<WorkloadRecord> _workload = new();
    private string? _currentProject;
    private string? _currentProcessId;
    private string? _currentWorkProject;
    private readonly InactivityMonitor _monitor = new();

    private static readonly Regex NumRegex = new("^[0-9]+$");

    public MainWindow()
    {
        InitializeComponent();
        _password = AuthService.SessionPassword
            ?? throw new InvalidOperationException("未登录，拒绝访问。");
        LoadWindowSize();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { SaveWindowSize(); _monitor.Dispose(); };
    }

    // 记住用户调整过的窗口尺寸（存注册表 HKCU，与加密数据无关）
    private void LoadWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ProjectRecorder");
            if (key == null) return;
            if (key.GetValue("MainW") is int w && w >= 760 && w <= 1600) Width = w;
            if (key.GetValue("MainH") is int h && h >= 560 && h <= 1000) Height = h;
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
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        };
        _monitor.Start();

        ShowHome();
    }

    // ---------- 视图切换 ----------
    private void ShowOnly(System.Windows.Controls.Grid view)
    {
        ViewHome.Visibility = Visibility.Collapsed;
        ViewProjects.Visibility = Visibility.Collapsed;
        ViewProcessList.Visibility = Visibility.Collapsed;
        ViewFlow.Visibility = Visibility.Collapsed;
        ViewWorkloadList.Visibility = Visibility.Collapsed;
        ViewWorkloadDetail.Visibility = Visibility.Collapsed;
        view.Visibility = Visibility.Visible;
    }

    private void ShowHome()
    {
        _currentProject = null;
        _currentProcessId = null;
        _currentWorkProject = null;
        ShowOnly(ViewHome);
        BtnGoProjects.Focus();
    }

    private void ShowProjects(string? keep = null)
    {
        _currentProject = keep;
        _currentProcessId = null;
        ShowOnly(ViewProjects);
        RefreshProjects(keep);
        LstProjects.Focus();
    }

    private void ShowProcessList(string project, string? keepProcessId = null)
    {
        _currentProject = project;
        _currentProcessId = keepProcessId;
        ShowOnly(ViewProcessList);
        TxtProcessListTitle.Text = project;
        RefreshProcessList(keepProcessId);
    }

    private void ShowFlow(string processId)
    {
        _currentProcessId = processId;
        ShowOnly(ViewFlow);
        RefreshFlow();
        BtnAddStep.Focus();
    }

    // ---------- 视图0：主界面 ----------
    private void BtnGoProjects_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowProjects();
    }

    private void BtnGoStats_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowWorkloadList();
    }

    private void BtnBackHome_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowHome();
    }

    // ---------- 视图1：项目列表 ----------
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

        LstProjects.ItemsSource = null;
        LstProjects.ItemsSource = _projects;
        if (keep != null && _projects.Contains(keep))
            LstProjects.SelectedItem = keep;
    }

    // 点右上角添加后弹窗输入
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
        if (_projects.Contains(name))
        {
            MessageBox.Show("该项目已存在，双击可直接进入。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        // 添加项目后直接进入该项目
        ShowProcessList(name);
    }

    private void LstProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 只有点中卡片才进入，点空白区域忽略
        if (FindListBoxItem(e.OriginalSource as DependencyObject) == null) return;
        OpenProject();
    }

    private void OpenProject()
    {
        _monitor.NotifyActivity();
        string? project = LstProjects.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(project))
        {
            MessageBox.Show("请先点选一个项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowProcessList(project!);
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

    private static System.Windows.Controls.ListBoxItem? FindListBoxItem(DependencyObject? src)
    {
        DependencyObject? d = src;
        int guard = 0;
        while (d != null && d is not System.Windows.Controls.ListBoxItem && guard++ < 50)
            d = SafeParent(d);
        return d as System.Windows.Controls.ListBoxItem;
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

    private void MenuDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        string? project = LstProjects.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(project))
        {
            MessageBox.Show("请先点选要删除的项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除项目「{project}」吗？\n其下全部工序、操作流程及工作量记录将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        _projects.Remove(project!);
        try
        {
            DataStore.SaveProjects(_projects, _password);
            var processes = DataStore.LoadProcesses(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveProcesses(processes, _password);
            var workload = DataStore.LoadWorkload(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveWorkload(workload, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshProjects();
    }

    // ---------- 视图2：工序列表 ----------
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
        if (keepProcessId != null)
        {
            var keep = list.FirstOrDefault(x => x.Id == keepProcessId);
            if (keep != null) LstProcesses.SelectedItem = keep;
        }
        bool empty = list.Count == 0;
        TxtProcessEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnBackToProjects_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowProjects(_currentProject);
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

        _processes.Add(new ProcessItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentProject!,
            ProcessName = dlg.ProcessName,
            UpdatedTime = DateTime.Now
        });
        try
        {
            DataStore.SaveProcesses(_processes, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshProcessList();
    }

    private void LstProcesses_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 只有点中卡片才进入，点空白区域忽略
        if (FindListBoxItem(e.OriginalSource as DependencyObject) == null) return;
        OpenProcess();
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

    private void OpenProcess()
    {
        _monitor.NotifyActivity();
        if (LstProcesses.SelectedItem is not ProcessItem sel)
        {
            MessageBox.Show("请先点选一道工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowFlow(sel.Id);
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshProcessList();
    }

    // ---------- 视图3：工序详情 ----------
    private ProcessItem? FindCurrentProcess()
    {
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
            MessageBox.Show("该工序已不存在，将返回工序列表。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            if (!string.IsNullOrWhiteSpace(_currentProject))
                ShowProcessList(_currentProject!);
            else
                ShowProjects();
            return;
        }

        TxtFlowTitle.Text = $"{proc.ProjectName} / {proc.ProcessName}";
        LstSteps.ItemsSource = null;
        LstSteps.ItemsSource = proc.Steps;
        bool empty = proc.Steps.Count == 0;
        TxtStepEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
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

    private void BtnBackToProcessList_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (!string.IsNullOrWhiteSpace(_currentProject))
            ShowProcessList(_currentProject!, _currentProcessId);
        else
            ShowProjects();
    }

    private void BtnAddStep_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (FindCurrentProcess() == null) return;

        var dlg = new StepDialog(_monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var target = _processes.First(x => x.Id == _currentProcessId);
        target.Steps.Add(new FlowStep
        {
            Id = Guid.NewGuid().ToString(),
            Text = dlg.StepText,
            ImageData = dlg.ImageData,
            ImageName = dlg.ImageName
        });
        try
        {
            PersistProcesses();
        }
        catch (Exception ex)
        {
            target.Steps.RemoveAt(target.Steps.Count - 1);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshFlow();
    }

    // 卡片文字只读可选（可复制）；右键点在文字上走复制菜单，点卡片其他位置先选中（菜单里删除）
    private void LstSteps_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        for (DependencyObject? d = src; d != null; d = SafeParent(d))
        {
            if (d is System.Windows.Controls.TextBox) return;
        }

        var item = FindListBoxItem(src);
        if (item != null) item.IsSelected = true;
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

    // 点击图片放大查看
    private void StepImage_Click(object sender, MouseButtonEventArgs e)
    {
        _monitor.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is not FlowStep step) return;
        if (step.ImageData == null || step.ImageData.Length == 0) return;

        var proc = FindCurrentProcess();
        string title = proc != null ? $"{proc.ProcessName} 第 {step.Order} 步" : $"第 {step.Order} 步";
        new ImageViewerDialog(step.ImageData, title, _monitor) { Owner = this }.ShowDialog();
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshFlow();
    }

    // ---------- 工作量统计 ----------
    private void BtnBackHomeFromWork_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowHome();
    }

    // 左上导出：选日期 → 导出该日期全部工作量为 Excel（冻结标题行）
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        var dateDlg = new ExportDateDialog(_monitor) { Owner = this };
        if (dateDlg.ShowDialog() != true) return;

        List<WorkloadRecord> list;
        try
        {
            list = DataStore.LoadWorkload(_password)
                .Where(x => x.WorkDate.Date == dateDlg.SelectedDay)
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.ProcessName)
                .ThenBy(x => x.CreatedTime)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (list.Count == 0)
        {
            MessageBox.Show($"{dateDlg.SelectedDay:yyyy-MM-dd} 暂无工作量记录。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fileDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出工作量",
            Filter = "Excel 文件|*.xlsx",
            FileName = $"工作量_{dateDlg.SelectedDay:yyyy-MM-dd}.xlsx"
        };
        if (fileDlg.ShowDialog(this) != true) return;

        try
        {
            ExcelExporter.ExportWorkload(fileDlg.FileName, dateDlg.SelectedDay, list);
            MessageBox.Show($"导出成功，共 {list.Count} 条：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowWorkloadList()
    {
        _currentWorkProject = null;
        ShowOnly(ViewWorkloadList);
        RefreshWorkloadList();
        LstWorkProjects.Focus();
    }

    private void RefreshWorkloadList()
    {
        List<string> projects;
        try
        {
            projects = DataStore.LoadProjects(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        // 通用作为最后一个普通项目展示（固定，不可在此增删）
        var all = new List<string>(projects);
        if (!all.Contains(DataStore.FixedMachineProject))
            all.Add(DataStore.FixedMachineProject);

        LstWorkProjects.ItemsSource = null;
        LstWorkProjects.ItemsSource = all;
    }

    private void LstWorkProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindListBoxItem(e.OriginalSource as DependencyObject) == null) return;
        OpenWorkProject();
    }

    private void OpenWorkProject()
    {
        _monitor.NotifyActivity();
        string? project = LstWorkProjects.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(project))
        {
            MessageBox.Show("请先点选一个项目，或直接进入通用。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowWorkloadDetail(project!);
    }

    private void BtnBackToWorkload_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        ShowWorkloadList();
        if (!string.IsNullOrWhiteSpace(_currentWorkProject) && _currentWorkProject != DataStore.FixedMachineProject)
            LstWorkProjects.SelectedItem = _currentWorkProject;
    }

    private void ShowWorkloadDetail(string project)
    {
        _currentWorkProject = project;
        ShowOnly(ViewWorkloadDetail);
        TxtWorkloadTitle.Text = $"{project} - 工作量";
        _selectedWorkDate = WorkloadRecord.GetShiftDate(DateTime.Now);
        RefreshWorkDateButton();
        RefreshWorkCards();
        BtnPickDate.Focus();
    }

    private DateTime _selectedWorkDate = DateTime.Today;

    private void RefreshWorkDateButton()
    {
        BtnPickDate.Content = _selectedWorkDate.ToString("yyyy-MM-dd");
    }

    private void BtnPickDate_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        CalWork.SelectedDate = _selectedWorkDate;
        CalWork.DisplayDate = _selectedWorkDate;
        PopCalendar.IsOpen = true;
    }

    private void CalWork_SelectedDatesChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (CalWork.SelectedDate.HasValue)
        {
            _selectedWorkDate = CalWork.SelectedDate.Value.Date;
            RefreshWorkDateButton();
        }
        PopCalendar.IsOpen = false;
        RefreshWorkCards();
    }

    private sealed class WorkCard
    {
        public WorkloadProcess Process { get; set; } = null!;
        public int TotalQty { get; set; }
    }

    private void RefreshWorkCards()
    {
        List<WorkloadProcess> procs;
        List<WorkloadRecord> records;
        try
        {
            procs = DataStore.LoadWorkloadProcesses(_password)
                .Where(x => x.ProjectName == _currentWorkProject)
                .ToList();
            records = DataStore.LoadWorkload(_password)
                .Where(x => x.ProjectName == _currentWorkProject)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var cards = procs
            .OrderBy(x => x.Order)
            .ThenBy(x => x.CreatedTime)
            .Select(p => new WorkCard
        {
            Process = p,
            TotalQty = records.Where(r => r.ProcessName == p.ProcessName).Sum(r => r.Quantity)
        }).ToList();

        LstWorkProcess.ItemsSource = null;
        LstWorkProcess.ItemsSource = cards;
        bool empty = cards.Count == 0;
        TxtWProcessEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        int total = records.Sum(r => r.Quantity);
        TxtWSummary.Text = $"{_currentWorkProject} 累计完成：{total}";
    }

    private void BtnAddWProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentWorkProject)) return;

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

        if (procs.Any(x => x.ProjectName == _currentWorkProject && x.ProcessName == dlg.ProcessName))
        {
            MessageBox.Show("该工序已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        procs.Add(new WorkloadProcess
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentWorkProject!,
            ProcessName = dlg.ProcessName,
            Order = procs.Where(x => x.ProjectName == _currentWorkProject).Select(x => x.Order).DefaultIfEmpty(0).Max() + 1,
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

    private static System.Windows.Controls.TextBox? FindQtyBox(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        int guard = 0;
        while (queue.Count > 0 && guard++ < 200)
        {
            var d = queue.Dequeue();
            if (d is System.Windows.Controls.TextBox tb && tb.Name == "QtyBox") return tb;
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
        while (d != null && d is not System.Windows.Controls.ListBoxItem && guard++ < 30)
            d = VisualTreeHelper.GetParent(d);
        return d;
    }

    private int ReadCardQty(object sender, out System.Windows.Controls.TextBox? box)
    {
        box = null;
        var container = FindListBoxItemContainer(sender);
        if (container != null) box = FindQtyBox(container);
        if (box == null) return 1;
        if (!int.TryParse(box.Text.Trim(), out int v) || v <= 0) return 1;
        return v;
    }

    private void AdjustCardQty(object sender, int delta)
    {
        _monitor.NotifyActivity();
        int v = ReadCardQty(sender, out var box);
        v += delta;
        if (v < 1) v = 1;
        if (v > 999999) v = 999999;
        if (box != null) box.Text = v.ToString();
    }

    private void BtnWCardMinus_Click(object sender, RoutedEventArgs e) => AdjustCardQty(sender, -1);
    private void BtnWCardPlus_Click(object sender, RoutedEventArgs e) => AdjustCardQty(sender, 1);

    private void TxtQty_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !NumRegex.IsMatch(e.Text);
    }

    private void BtnWCardSave_Click(object sender, RoutedEventArgs e)
    {
        _monitor.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_currentWorkProject))
        {
            MessageBox.Show("请先返回选择项目。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if ((sender as FrameworkElement)?.DataContext is not WorkCard card)
            return;

        int qty = ReadCardQty(sender, out var box);
        if (box != null && (!int.TryParse(box.Text.Trim(), out qty) || qty <= 0))
        {
            MessageBox.Show("数量必须为正整数。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            return;
        }

        var rec = new WorkloadRecord
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _currentWorkProject!,
            ProcessName = card.Process.ProcessName,
            WorkDate = _selectedWorkDate.Date,
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
                .Where(x => x.ProjectName == _currentWorkProject)
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
            all.RemoveAll(x => x.ProjectName == _currentWorkProject && x.ProcessName == sel.ProcessName);
            DataStore.SaveWorkload(all, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshWorkCards();
    }
}
