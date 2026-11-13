using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 分组选择/管理弹窗：新建、重命名、删除分组，并返回选中的分组。
/// 删除仅允许空分组；重命名会通过 Renames 返回给调用方迁移条目。
/// </summary>
public partial class GroupPickerDialog : Window
{
    private readonly InactivityMonitor? _monitor;
    private readonly IReadOnlyDictionary<string, int> _counts;
    private readonly List<(string From, string To)> _renames = new();
    private readonly List<string> _deleted = new();

    /// <summary>操作后的最终分组列表。</summary>
    public List<string> Groups { get; private set; } = new();
    /// <summary>确定时选中的分组。</summary>
    public string SelectedGroup { get; private set; } = string.Empty;
    /// <summary>发生的重命名（旧名 → 新名），调用方据此迁移条目。</summary>
    public IReadOnlyList<(string From, string To)> Renames => _renames;
    /// <summary>被删除的分组名（仅空分组可删）。</summary>
    public IReadOnlyList<string> DeletedGroups => _deleted;

    public GroupPickerDialog(string title, List<string> groups, string currentGroup,
        IReadOnlyDictionary<string, int>? counts = null, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        Title = title;
        _monitor = monitor;
        _counts = counts ?? new Dictionary<string, int>();
        Groups = (groups ?? new List<string>())
            .Select(g => (g ?? string.Empty).Trim())
            .Where(g => g.Length > 0)
            .Distinct()
            .ToList();
        if (Groups.Count == 0) Groups.Add(DataStore.DefaultGroup);

        HookActivity();
        Rebind(string.IsNullOrWhiteSpace(currentGroup) ? Groups[0] : currentGroup);
    }

    private void HookActivity()
    {
        if (_monitor == null) return;
        PreviewMouseMove += (s, e) => _monitor.NotifyActivity();
        PreviewMouseDown += (s, e) => _monitor.NotifyActivity();
        PreviewMouseUp += (s, e) => _monitor.NotifyActivity();
        PreviewMouseWheel += (s, e) => _monitor.NotifyActivity();
        PreviewKeyDown += (s, e) => _monitor.NotifyActivity();
        PreviewKeyUp += (s, e) => _monitor.NotifyActivity();
        PreviewTextInput += (s, e) => _monitor.NotifyActivity();
    }

    private void Rebind(string? select)
    {
        LstGroups.ItemsSource = null;
        LstGroups.ItemsSource = Groups;
        if (select != null && Groups.Contains(select))
            LstGroups.SelectedItem = select;
        else if (Groups.Count > 0)
            LstGroups.SelectedItem = Groups[0];
    }

    private string? Selected => LstGroups.SelectedItem as string;

    private void BtnNewGroup_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var dlg = new NameInputDialog("新建分组", "分组名称 *", string.Empty, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (Groups.Contains(dlg.Value))
        {
            MessageBox.Show("该分组名称已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Groups.Add(dlg.Value);
        Rebind(dlg.Value);
    }

    private void BtnRenameGroup_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        string? old = Selected;
        if (old == null)
        {
            MessageBox.Show("请先选择要重命名的分组。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new NameInputDialog("重命名分组", "分组名称 *", old, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Value == old) return;
        if (Groups.Contains(dlg.Value))
        {
            MessageBox.Show("该分组名称已存在。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int i = Groups.IndexOf(old);
        Groups[i] = dlg.Value;
        _renames.Add((old, dlg.Value));
        Rebind(dlg.Value);
    }

    private void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        string? sel = Selected;
        if (sel == null)
        {
            MessageBox.Show("请先选择要删除的分组。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Groups.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个分组。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_counts.TryGetValue(sel, out int n) && n > 0)
        {
            MessageBox.Show($"分组「{sel}」下还有 {n} 个条目，请先清空或移走后删除。",
                "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除空分组「{sel}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        Groups.Remove(sel);
        _deleted.Add(sel);
        Rebind(Groups[0]);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedGroup = Selected ?? (Groups.Count > 0 ? Groups[0] : DataStore.DefaultGroup);
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
