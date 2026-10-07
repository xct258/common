using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 思维导图独立弹出窗口：顶部「分组」下拉查看，分组独立维护（与项目列表无关）。
/// 新建导图时弹窗单独选分组（可现场新建分组）。
/// </summary>
public partial class MindMapWindow : Window
{
    private readonly string _password;
    private readonly InactivityMonitor? _monitor;

    private List<MindMap> _allMaps = new();
    private List<string> _groups = new();
    private string _currentGroup = DataStore.DefaultGroup;
    private string? _currentMindMapId;
    private bool _loadingGroups = true;

    public MindMapWindow(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _password = AuthService.SessionPassword
            ?? throw new InvalidOperationException("未登录，拒绝访问。");
        _monitor = monitor;

        MindView.Changed += OnMindMapChanged;
        MindView.ZoomChanged += scale => TxtMindZoom.Text = $"{Math.Round(scale * 100)}%";
        MindView.Monitor = _monitor;

        Loaded += (_, _) =>
        {
            HookActivity();
            LoadGroups();
            LoadMaps();
        };
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

    // ---------- 分组（独立维护） ----------

    private void LoadGroups()
    {
        try
        {
            _groups = DataStore.LoadMindGroups(_password);
        }
        catch (InvalidOperationException)
        {
            _groups = new List<string> { DataStore.DefaultGroup };
        }
        BindGroups();
    }

    private void BindGroups()
    {
        if (_groups.Count == 0) _groups.Add(DataStore.DefaultGroup);
        if (!_groups.Contains(_currentGroup)) _currentGroup = _groups[0];

        _loadingGroups = true;
        CmbGroup.ItemsSource = null;
        CmbGroup.ItemsSource = _groups;
        CmbGroup.SelectedItem = _currentGroup;
        _loadingGroups = false;
    }

    private void CmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (_loadingGroups || CmbGroup.SelectedItem is not string group) return;
        if (group == _currentGroup) return;
        _currentGroup = group;
        _currentMindMapId = null;
        RefreshMindMapList();
    }

    private static string GroupKey(string? project)
        => string.IsNullOrWhiteSpace(project) ? DataStore.DefaultGroup : project!.Trim();

    private List<MindMap> GroupMaps()
        => _allMaps.Where(x => GroupKey(x.ProjectName) == _currentGroup).ToList();

    private Dictionary<string, int> CountsByGroup()
        => _allMaps.GroupBy(x => GroupKey(x.ProjectName))
            .ToDictionary(g => g.Key, g => g.Count());

    // 应用分组管理的改名/删除：迁移条目、保存分组、刷新下拉（不刷新卡片列表）
    private void ApplyGroupDialog(GroupPickerDialog dlg)
    {
        bool itemsChanged = false;
        foreach (var (from, to) in dlg.Renames)
        {
            foreach (var m in _allMaps.Where(x => GroupKey(x.ProjectName) == from))
            {
                m.ProjectName = to;
                itemsChanged = true;
            }
        }
        if (itemsChanged)
        {
            try
            {
                DataStore.SaveMindMaps(_allMaps, _password);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        _groups = new List<string>(dlg.Groups);
        try
        {
            DataStore.SaveMindGroups(_groups, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"分组保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _currentGroup = _groups.Contains(dlg.SelectedGroup) ? dlg.SelectedGroup : _groups[0];
        BindGroups();
    }

    private void BtnManageGroups_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var dlg = new GroupPickerDialog("管理分组", _groups, _currentGroup, CountsByGroup(), _monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        ApplyGroupDialog(dlg);
        RefreshMindMapList();
    }

    // ---------- 导图 ----------

    private void LoadMaps()
    {
        try
        {
            _allMaps = DataStore.LoadMindMaps(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _allMaps = new List<MindMap>();
        }
        RefreshMindMapList(_currentMindMapId);
    }

    private void RefreshMindMapList(string? keepId = null)
    {
        var maps = GroupMaps();
        LstMindMaps.ItemsSource = null;
        LstMindMaps.ItemsSource = maps;
        TxtMindPopEmpty.Visibility = maps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MindEmptyPanel.Visibility = maps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        MindMap? keep = keepId == null ? null : maps.FirstOrDefault(x => x.Id == keepId);
        if (keep == null && maps.Count > 0) keep = maps[0];
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

    private void OnMindMapChanged()
    {
        var map = _allMaps.FirstOrDefault(x => x.Id == _currentMindMapId);
        if (map == null) return;
        map.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveMindMaps(_allMaps, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnMindMapSwitch_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (GroupMaps().Count == 0)
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
        _monitor?.NotifyActivity();
        AddMindMap();
    }

    private void AddMindMap()
    {
        var nameDlg = new NameInputDialog("新建思维导图", "导图名称 *", string.Empty, _monitor) { Owner = this };
        if (nameDlg.ShowDialog() != true) return;

        // 单独选分组（可现场新建）
        var gdlg = new GroupPickerDialog("选择分组", _groups, _currentGroup, CountsByGroup(), _monitor) { Owner = this };
        if (gdlg.ShowDialog() != true) return;
        ApplyGroupDialog(gdlg);
        string group = _currentGroup;

        if (_allMaps.Any(x => GroupKey(x.ProjectName) == group && x.Name == nameDlg.Value))
        {
            MessageBox.Show("该分组下已存在同名导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var map = new MindMap
        {
            Name = nameDlg.Value,
            ProjectName = group,
            Root = new MindNode { Title = "中心主题" }
        };
        _allMaps.Add(map);
        try
        {
            DataStore.SaveMindMaps(_allMaps, _password);
        }
        catch (Exception ex)
        {
            _allMaps.Remove(map);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshMindMapList(map.Id);
        MindPop.IsOpen = false;
        MindView.BeginEditRoot();
    }

    // 把选中的导图移动到其它分组
    private void BtnMoveMindMapGroup_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstMindMaps.SelectedItem is not MindMap map)
        {
            MessageBox.Show("请先点选要修改分组的导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gdlg = new GroupPickerDialog("修改分组", _groups, GroupKey(map.ProjectName), CountsByGroup(), _monitor) { Owner = this };
        if (gdlg.ShowDialog() != true) return;
        ApplyGroupDialog(gdlg);
        string group = _currentGroup;
        if (GroupKey(map.ProjectName) == group) return;

        map.ProjectName = group;
        map.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveMindMaps(_allMaps, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshMindMapList(map.Id);
    }

    private void MenuRenameMindMap_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstMindMaps.SelectedItem is not MindMap map)
        {
            MessageBox.Show("请先点选要重命名的导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new NameInputDialog("重命名导图", "导图名称 *", map.Name, _monitor) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Value == map.Name) return;
        if (GroupMaps().Any(x => x.Id != map.Id && x.Name == dlg.Value))
        {
            MessageBox.Show("该分组下已存在同名导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string oldName = map.Name;
        map.Name = dlg.Value;
        map.UpdatedTime = DateTime.Now;
        try
        {
            DataStore.SaveMindMaps(_allMaps, _password);
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
        _monitor?.NotifyActivity();
        if (LstMindMaps.SelectedItem is not MindMap map)
        {
            MessageBox.Show("请先点选要删除的导图。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除导图「{map.Name}」吗？\n其全部节点将被一并删除。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        int index = _allMaps.IndexOf(map);
        _allMaps.Remove(map);
        try
        {
            DataStore.SaveMindMaps(_allMaps, _password);
        }
        catch (Exception ex)
        {
            _allMaps.Insert(Math.Min(index, _allMaps.Count), map);
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _currentMindMapId = null;
        RefreshMindMapList();
    }

    private void BtnMindZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        MindView.ZoomIn();
    }

    private void BtnMindZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        MindView.ZoomOut();
    }

    private void BtnMindFit_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        MindView.Fit();
    }

    private void BtnMindMapHelp_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        new MindMapHelpDialog(_monitor) { Owner = this }.ShowDialog();
        _monitor?.NotifyActivity();
    }

    private void BtnMindExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (CurrentMap() == null || MindView.Map == null)
        {
            MessageBox.Show("请先选择或新建一张思维导图。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        var png = new MenuItem { Header = "导出为图片（PNG）" };
        png.Click += (_, _) => ExportMindMapPng();
        menu.Items.Add(png);
        var yaml = new MenuItem { Header = "导出为 YAML（适合给 AI）" };
        yaml.Click += (_, _) => ExportMindMapYaml();
        menu.Items.Add(yaml);

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private MindMap? CurrentMap()
        => _allMaps.FirstOrDefault(x => x.Id == _currentMindMapId);

    private void ExportMindMapPng()
    {
        var map = CurrentMap();
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

    private void ExportMindMapYaml()
    {
        var map = CurrentMap();
        if (map == null) return;

        var fileDlg = new SaveFileDialog
        {
            Title = "导出为 YAML",
            Filter = "YAML 文件|*.yaml;*.yml",
            FileName = SafeFileName(map.Name) + ".yaml"
        };
        if (fileDlg.ShowDialog(this) != true) return;

        try
        {
            int count = MindMapOutlineExporter.ExportYaml(fileDlg.FileName, map);
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
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');
        return result.Length == 0 ? "思维导图" : result;
    }
}
