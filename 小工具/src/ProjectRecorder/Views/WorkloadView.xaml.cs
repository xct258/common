using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProjectRecorder.Helpers;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 工作量视图：当前班次卡片计数、班次切换/补录、工序增删排序。
/// 项目由 Reload(project) 传入；进入页面时自动回到当前班次。
/// </summary>
public partial class WorkloadView : UserControl
{
    private static readonly Regex NumRegex = new("^[0-9]+$");

    private DataService? _data;
    private InactivityMonitor? _monitor;
    private string? _project;
    private DateTime _moment = DateTime.Now;
    private DateTime _date = DateTime.Today;
    private string _shift = WorkloadRecord.DayShift;
    private bool _backfill;

    public WorkloadView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>进入工作量页：按当前项目、当前班次重新统计。</summary>
    public void Reload(string? project)
    {
        _project = project;
        if (_data == null) return;

        var now = DateTime.Now;
        _moment = now;
        _date = WorkloadRecord.GetShiftDate(now);
        _shift = WorkloadRecord.GetShiftName(now);
        _backfill = false;
        UpdateShiftButton();
        RefreshCards();
    }

    private bool IsCurrentShift()
    {
        var now = DateTime.Now;
        return _date.Date == WorkloadRecord.GetShiftDate(now).Date
               && _shift == WorkloadRecord.GetShiftName(now);
    }

    private void UpdateShiftButton()
    {
        BtnWorkShift.Content = $"{_date:yyyy-MM-dd} {_shift}";
        var vis = (_backfill || !IsCurrentShift()) ? Visibility.Visible : Visibility.Collapsed;
        TxtWorkBackfill.Visibility = vis;
        BtnWorkNow.Visibility = vis;
    }

    // 点顶部班次标题：只选日期+班次补录，之后卡片添加即记为补录（导出明细标记“补录”）
    private void BtnWorkShift_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var now = DateTime.Now;
        var dlg = new ShiftPickerDialog(now, WorkloadRecord.GetShiftName(now), _monitor)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;

        _date = dlg.SelectedDate.Date;
        _shift = dlg.SelectedShift;
        _backfill = true;
        // 补录无具体时间：白班按 08:30、夜班按 21:00 占位，仅用于排序/展示
        _moment = _date.AddHours(_shift == WorkloadRecord.DayShift ? 8.5 : 21);
        UpdateShiftButton();
        RefreshCards();
    }

    // 快速回到当前班次（结束补录）
    private void BtnWorkNow_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var now = DateTime.Now;
        _moment = now;
        _date = WorkloadRecord.GetShiftDate(now);
        _shift = WorkloadRecord.GetShiftName(now);
        _backfill = false;
        UpdateShiftButton();
        RefreshCards();
    }

    private void RefreshCards()
    {
        if (_data == null) return;
        var procs = _data.LoadWorkloadProcesses();
        if (procs == null) return;
        var records = _data.LoadWorkload();
        if (records == null) return;

        // 只统计当前操作的班次：班次日期 + 白/夜班都一致；点标题可切换班次
        var cards = WorkloadService.ComputeCards(
            procs.Where(x => x.ProjectName == _project),
            records.Where(x => x.ProjectName == _project),
            _date, _shift);

        // 通用工作量页末尾固定追加“其它”卡片：点击手动录入项目 + 时间
        if (_project == DataStore.FixedMachineProject)
        {
            var otherRecords = records
                .Where(x => x.IsOther && x.WorkDate.Date == _date && x.Shift == _shift)
                .ToList();
            cards.Add(new WorkCard
            {
                IsOther = true,
                Process = new WorkloadProcess
                {
                    Id = "__other__",
                    ProjectName = _project,
                    ProcessName = WorkloadRecord.OtherProcessName,
                    Order = int.MaxValue,
                    CreatedTime = DateTime.MaxValue
                },
                ShiftQty = otherRecords.Sum(x => x.Quantity)
            });
        }

        LstWorkProcess.ItemsSource = null;
        LstWorkProcess.ItemsSource = cards;
        TxtWProcessEmpty.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnAddWProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_project) || _data == null) return;

        var dlg = new AddProcessDialog(_monitor) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var procs = _data.LoadWorkloadProcesses();
        if (procs == null) return;

        if (procs.Any(x => x.ProjectName == _project && x.ProcessName == dlg.ProcessName))
        {
            MessageBox.Show("该工序已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        procs.Add(new WorkloadProcess
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _project!,
            ProcessName = dlg.ProcessName,
            Order = procs.Where(x => x.ProjectName == _project).Select(x => x.Order).DefaultIfEmpty(0).Max() + 1,
            CreatedTime = DateTime.Now
        });
        if (!_data.SaveWorkloadProcesses(procs)) return;

        RefreshCards();
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

    private int ReadCardQty(object sender, out TextBox? box)
    {
        box = null;
        var container = VisualTreeHelpers.FindListBoxItemContainer(sender);
        if (container != null) box = FindQtyBox(container);
        if (box == null) return 1;
        if (!int.TryParse(box.Text.Trim(), out int v)) return 1;
        return v;
    }

    private void AdjustCardQty(object sender, int delta)
    {
        _monitor?.NotifyActivity();
        int v = WorkloadService.StepQuantity(ReadCardQty(sender, out var box), delta);
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
        _monitor?.NotifyActivity();
        if (string.IsNullOrWhiteSpace(_project) || _data == null)
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
        qty = WorkloadService.ClampDeduction(card.ShiftQty, qty);
        if (qty == 0) return;

        var rec = new WorkloadRecord
        {
            Id = Guid.NewGuid().ToString(),
            ProjectName = _project!,
            ProcessName = card.Process.ProcessName,
            WorkDate = _date,
            Shift = _shift,
            Quantity = qty,
            Notes = string.Empty,
            IsBackfill = _backfill,
            CreatedTime = (_backfill || !IsCurrentShift()) ? _moment : DateTime.Now
        };

        var all = _data.LoadWorkload();
        if (all == null) return;
        all.Add(rec);
        if (!_data.SaveWorkload(all)) return;

        box.Text = "1";
        RefreshCards();
    }

    private void LstWorkProcess_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject);
        if (item == null)
        {
            // 空白处右键：不选中也不弹菜单
            e.Handled = true;
            return;
        }
        // “其它”卡片没有工序菜单，右键不弹菜单
        if ((item.DataContext as WorkCard)?.IsOther == true)
        {
            item.IsSelected = true;
            e.Handled = true;
            return;
        }
        item.IsSelected = true;
    }

    private void LstWorkProcess_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((LstWorkProcess.SelectedItem as WorkCard)?.IsOther == true)
            e.Handled = true;
    }

    // 点击末尾“其它”卡片：弹窗录入项目 + 时间
    private void WorkCard_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkCard card || !card.IsOther)
            return;
        e.Handled = true;
        _monitor?.NotifyActivity();
        if (_data == null) return;

        var dlg = new OtherWorkDialog(_monitor) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var all = _data.LoadWorkload();
        if (all == null) return;
        all.Add(new WorkloadRecord
        {
            ProjectName = dlg.ProjectName,
            ProcessName = WorkloadRecord.OtherProcessName,
            WorkDate = _date,
            Shift = _shift,
            Quantity = dlg.Hours,
            IsOther = true,
            IsBackfill = _backfill,
            CreatedTime = (_backfill || !IsCurrentShift()) ? _moment : DateTime.Now
        });
        if (!_data.SaveWorkload(all)) return;

        RefreshCards();
    }

    // 空白处松开右键不弹菜单（菜单在松开时触发，只拦按下不够）
    private void ListBlankUp(object sender, MouseButtonEventArgs e)
    {
        if (VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject) == null)
            e.Handled = true;
    }

    // 右键卡片 →「查看执行明细」：弹窗列出该工序本班的每次执行时间（含补录标记）
    private void MenuWProcessDetail_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstWorkProcess.SelectedItem is not WorkCard card)
        {
            MessageBox.Show("请先右键点选要查看的工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_data == null) return;

        var all = _data.LoadWorkload();
        if (all == null) return;
        var records = all
            .Where(x => x.ProjectName == _project
                        && x.ProcessName == card.Process.ProcessName
                        && x.WorkDate.Date == _date
                        && x.Shift == _shift)
            .ToList();

        if (records.Count == 0)
        {
            MessageBox.Show("该工序在本班暂无执行记录。", "执行明细",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new WorkDetailDialog(_project ?? string.Empty, card.Process.ProcessName,
            _date, _shift, records, _monitor) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void MoveWProcess(object sender, int delta)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;
        var card = LstWorkProcess.SelectedItem as WorkCard;
        if (card == null)
        {
            if ((sender as FrameworkElement)?.DataContext is WorkCard ctx)
                card = ctx;
        }
        if (card == null) return;

        var mineAll = _data.LoadWorkloadProcesses();
        if (mineAll == null) return;
        var mine = mineAll
            .Where(x => x.ProjectName == _project)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.CreatedTime)
            .ToList();

        int i = mine.FindIndex(x => x.Id == card.Process.Id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= mine.Count) return;

        var all = _data.LoadWorkloadProcesses();
        if (all == null) return;
        var a = all.First(x => x.Id == mine[i].Id);
        var b = all.First(x => x.Id == mine[j].Id);
        (a.Order, b.Order) = (b.Order, a.Order);
        if (!_data.SaveWorkloadProcesses(all)) return;
        RefreshCards();
        var moved = (LstWorkProcess.ItemsSource as List<WorkCard>)
            ?.FirstOrDefault(x => x.Process.Id == card.Process.Id);
        if (moved != null) LstWorkProcess.SelectedItem = moved;
    }

    private void MenuWProcessUp_Click(object sender, RoutedEventArgs e) => MoveWProcess(sender, -1);
    private void MenuWProcessDown_Click(object sender, RoutedEventArgs e) => MoveWProcess(sender, 1);

    private void MenuDeleteWProcess_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if ((LstWorkProcess.SelectedItem as WorkCard)?.Process is not WorkloadProcess sel)
        {
            MessageBox.Show("请先右键点选要删除的工序。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_data == null) return;

        var r = MessageBox.Show($"确定删除工序「{sel.ProcessName}」吗？\n其工作量记录将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        var procs = _data.LoadWorkloadProcesses();
        if (procs == null) return;
        procs.RemoveAll(x => x.Id == sel.Id);
        if (!_data.SaveWorkloadProcesses(procs, "删除失败")) return;
        var all = _data.LoadWorkload();
        if (all == null) return;
        all.RemoveAll(x => x.ProjectName == _project && x.ProcessName == sel.ProcessName);
        if (!_data.SaveWorkload(all, "删除失败")) return;
        RefreshCards();
    }
}
