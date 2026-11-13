using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProjectRecorder.Helpers;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 工序视图：左列工序增删改排序，右列步骤详情与配图查看。
/// 当前选中工序在本视图内保持；切换项目时由 MainWindow 调 ResetSelection。
/// </summary>
public partial class ProcessView : UserControl
{
    private DataService? _data;
    private InactivityMonitor? _monitor;
    private string? _project;
    private List<ProcessItem> _processes = new();
    private string? _currentProcessId;

    public ProcessView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>进入工序页：按当前项目加载；同项目内保留上次选中的工序。</summary>
    public void Reload(string? project)
    {
        _project = project;
        RefreshProcessList(_currentProcessId);
    }

    /// <summary>切换/关闭项目时清空选中，下次进入默认选第一个工序。</summary>
    public void ResetSelection() => _currentProcessId = null;

    private List<ProcessItem> CurrentProcesses()
    {
        if (_data == null) return new List<ProcessItem>();
        var loaded = _data.LoadProcesses();
        _processes = loaded ?? new List<ProcessItem>();
        return _processes.Where(x => x.ProjectName == _project).ToList();
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
        _monitor?.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_project) || _data == null) return;

        var dlg = new AddProcessDialog(_monitor) { Owner = Window.GetWindow(this) };
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
            ProjectName = _project!,
            ProcessName = dlg.ProcessName,
            UpdatedTime = DateTime.Now
        };
        _processes.Add(item);
        if (!_data.SaveProcesses(_processes)) return;

        RefreshProcessList(item.Id);
    }

    private void LstProcesses_RightButtonDown(object sender, MouseButtonEventArgs e)
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

    private void MenuDeleteProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstProcesses.SelectedItem is not ProcessItem sel)
        {
            MessageBox.Show("请先点选要删除的工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_data == null) return;

        var r = MessageBox.Show($"确定删除工序「{sel.ProcessName}」吗？\n其操作流程将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        var all = _data.LoadProcesses();
        if (all != null)
        {
            all.RemoveAll(x => x.Id == sel.Id);
            if (_data.SaveProcesses(all, "删除失败"))
                _data.CleanupUnusedImages(all);
        }
        _currentProcessId = null;
        RefreshProcessList();
    }

    private void MenuMoveProcessUp_Click(object sender, RoutedEventArgs e) => MoveProcess(-1);
    private void MenuMoveProcessDown_Click(object sender, RoutedEventArgs e) => MoveProcess(1);

    // 在同项目工序序列内上移/下移（全局列表中交换位置后保存）
    private void MoveProcess(int delta)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;
        if (LstProcesses.SelectedItem is not ProcessItem sel) return;
        var all = _data.LoadProcesses();
        if (all == null) return;
        var mine = all.Where(x => x.ProjectName == _project).ToList();
        int i = mine.FindIndex(x => x.Id == sel.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= mine.Count) return;
        int gi = all.IndexOf(mine[i]);
        int gj = all.IndexOf(mine[j]);
        (all[gi], all[gj]) = (all[gj], all[gi]);
        if (!_data.SaveProcesses(all)) return;
        RefreshProcessList(sel.Id);
    }

    // ---------- 步骤详情 ----------

    private ProcessItem? FindCurrentProcess()
    {
        if (string.IsNullOrEmpty(_currentProcessId) || _data == null) return null;

        // 已在内存中就用内存对象：避免每次刷新/加步骤都重新解密解析整个 processes.dat（含全部图片），
        // 同时也是缩略图缓存命中的前提
        var cached = _processes.FirstOrDefault(x => x.Id == _currentProcessId);
        if (cached != null) return cached;

        var loaded = _data.LoadProcesses();
        if (loaded == null) return null;
        _processes = loaded;
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

    private bool PersistProcesses(string failurePrefix = "保存失败")
    {
        var proc = _processes.FirstOrDefault(x => x.Id == _currentProcessId);
        if (proc != null)
        {
            proc.UpdatedTime = DateTime.Now;
            for (int k = 0; k < proc.Steps.Count; k++)
                proc.Steps[k].Order = k + 1;
        }
        return _data != null && _data.SaveProcesses(_processes, failurePrefix);
    }

    private void BtnAddStep_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (FindCurrentProcess() == null || _data == null) return;

        var dlg = new StepDialog(_monitor) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var target = _processes.First(x => x.Id == _currentProcessId);
        var step = new FlowStep
        {
            Id = Guid.NewGuid().ToString(),
            Text = dlg.StepText,
            Images = new List<StepImage>(dlg.Images)
        };
        target.Steps.Add(step);
        if (!PersistProcesses())
        {
            target.Steps.Remove(step);
            return;
        }

        RefreshFlow();
        SelectStepById(step.Id);
    }

    // 卡片文字只读可选（可复制）；右键点在文字上走复制菜单，点卡片其他位置先选中（菜单里排序/删除）
    private void LstSteps_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        for (DependencyObject? d = src; d != null; d = VisualTreeHelpers.SafeParent(d))
        {
            if (d is TextBox) return;
        }

        var item = VisualTreeHelpers.FindListBoxItem(src);
        if (item != null) item.IsSelected = true;
    }

    private void MenuStepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void MenuStepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int delta)
    {
        _monitor?.NotifyActivity();
        if (LstSteps.SelectedItem is not FlowStep step) return;
        var proc = FindCurrentProcess();
        if (proc == null) return;

        int i = proc.Steps.FindIndex(x => x.Id == step.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= proc.Steps.Count) return;
        (proc.Steps[i], proc.Steps[j]) = (proc.Steps[j], proc.Steps[i]);
        if (!PersistProcesses())
        {
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
        _monitor?.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is not StepImage img) return;
        if (img.Data is not { Length: > 0 } && string.IsNullOrEmpty(img.Id)) return;
        if (_data == null) return;

        FlowStep? step = null;
        if (VisualTreeHelpers.FindListBoxItemContainer(sender) is ListBoxItem item)
            step = item.DataContext as FlowStep;

        var proc = FindCurrentProcess();
        string title = proc != null && step != null ? $"{proc.ProcessName} 第 {step.Order} 步" : "查看图片";
        var list = step?.Images;
        if (list != null && list.Count > 0)
        {
            int idx = list.FindIndex(x => x.Id == img.Id);
            if (idx < 0) idx = 0;
            new ImageViewerDialog(list, idx, title, _monitor) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        else
        {
            byte[]? data = _data.GetImageBytes(img);
            if (data == null || data.Length == 0) return;
            new ImageViewerDialog(data, title, _monitor) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        _monitor?.NotifyActivity();
    }

    private void DeleteStep(FlowStep step)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;
        var proc = FindCurrentProcess();
        if (proc == null) return;
        if (!proc.Steps.Any(x => x.Id == step.Id)) return;

        var r = MessageBox.Show($"确定删除第 {step.Order} 步吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        var target = _processes.First(x => x.Id == _currentProcessId);
        target.Steps.RemoveAll(x => x.Id == step.Id);
        if (!PersistProcesses("删除失败")) return;
        _data.CleanupUnusedImages(_processes);
        RefreshFlow();
    }
}
