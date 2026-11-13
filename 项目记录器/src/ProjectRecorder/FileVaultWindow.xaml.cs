using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 加密文件独立弹出窗口：顶部「分组」下拉查看，分组独立维护（与项目列表无关）。
/// 导入文件时弹窗单独选分组（可现场新建分组）。
/// 文件内容仍单独 AES 加密存 Data/files/&lt;id&gt;.enc。
/// </summary>
public partial class FileVaultWindow : Window
{
    private const long MaxImportFileSize = 200L * 1024 * 1024;

    private readonly string _password;
    private readonly InactivityMonitor? _monitor;
    private readonly Dictionary<string, CodeEditorWindow> _openEditors = new();

    private List<EncryptedFile> _allFiles = new();
    private List<string> _groups = new();
    private string _currentGroup = DataStore.DefaultGroup;
    private bool _loadingGroups = true;

    public FileVaultWindow(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _password = AuthService.SessionPassword
            ?? throw new InvalidOperationException("未登录，拒绝访问。");
        _monitor = monitor;

        Loaded += (_, _) =>
        {
            HookActivity();
            LoadGroups();
            RefreshEncryptedFiles();
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
            _groups = DataStore.LoadFileGroups(_password);
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
        RefreshEncryptedFiles();
    }

    private static string GroupKey(string? project)
        => string.IsNullOrWhiteSpace(project) ? DataStore.DefaultGroup : project!.Trim();

    private Dictionary<string, int> CountsByGroup()
        => _allFiles.GroupBy(x => GroupKey(x.ProjectName))
            .ToDictionary(g => g.Key, g => g.Count());

    // 应用分组管理的改名/删除：迁移条目、保存分组、刷新下拉（不刷新列表）
    private void ApplyGroupDialog(GroupPickerDialog dlg)
    {
        bool itemsChanged = false;
        foreach (var (from, to) in dlg.Renames)
        {
            foreach (var f in _allFiles.Where(x => GroupKey(x.ProjectName) == from))
            {
                f.ProjectName = to;
                itemsChanged = true;
            }
        }
        if (itemsChanged)
        {
            try
            {
                DataStore.SaveEncryptedFiles(_allFiles, _password);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        _groups = new List<string>(dlg.Groups);
        try
        {
            DataStore.SaveFileGroups(_groups, _password);
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
        RefreshEncryptedFiles();
    }

    // ---------- 列表 ----------

    private void RefreshEncryptedFiles()
    {
        try
        {
            _allFiles = DataStore.LoadEncryptedFiles(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _allFiles = new List<EncryptedFile>();
        }

        var files = _allFiles.Where(x => GroupKey(x.ProjectName) == _currentGroup).ToList();
        LstEncryptedFiles.ItemsSource = null;
        LstEncryptedFiles.ItemsSource = files;
        TxtFileEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        long total = files.Sum(x => x.Size);
        TxtFileVaultInfo.Text = files.Count == 0
            ? "导入后 AES-256 加密存本地，绝不明文落盘；导出的是解密副本，请妥善保管"
            : $"{_currentGroup}：共 {files.Count} 个，合计 {EncryptedFile.FormatSize(total)}；导出的是解密副本，请妥善保管";
    }

    private List<EncryptedFile> CurrentEncryptedFiles()
        => LstEncryptedFiles.ItemsSource as List<EncryptedFile> ?? new List<EncryptedFile>();

    private void BtnImportFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var dlg = new OpenFileDialog
        {
            Title = "选择要加密保存的文件",
            Multiselect = true,
            Filter = "所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        // 导入前先选分组（可现场新建）
        try
        {
            _allFiles = DataStore.LoadEncryptedFiles(_password);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "数据错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var gdlg = new GroupPickerDialog("选择分组", _groups, _currentGroup, CountsByGroup(), _monitor) { Owner = this };
        if (gdlg.ShowDialog() != true) return;
        ApplyGroupDialog(gdlg);
        string group = _currentGroup;

        var added = new List<EncryptedFile>();
        var overwritten = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();
        DuplicateChoice? batchChoice = null;
        var knownNames = new Dictionary<string, EncryptedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _allFiles.Where(x => GroupKey(x.ProjectName) == group))
            knownNames[f.Name] = f;

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
                var now = DateTime.Now;
                if (knownNames.TryGetValue(info.Name, out EncryptedFile? existing))
                {
                    DuplicateChoice choice;
                    if (batchChoice.HasValue)
                    {
                        choice = batchChoice.Value;
                    }
                    else
                    {
                        var dd = new DuplicateFileDialog(info.Name, _monitor) { Owner = this };
                        if (dd.ShowDialog() != true)
                        {
                            skipped.Add($"{info.Name}（跳过）");
                            continue;
                        }
                        choice = dd.Choice;
                        if (dd.ApplyToAll) batchChoice = choice;
                    }
                    if (choice == DuplicateChoice.Skip)
                    {
                        skipped.Add($"{info.Name}（跳过）");
                        continue;
                    }
                    if (choice == DuplicateChoice.Overwrite)
                    {
                        DataStore.SaveEncryptedFileContent(existing.Id, data, _password);
                        existing.Size = info.Length;
                        existing.OriginalName = info.Name;
                        existing.UpdatedTime = now;
                        overwritten.Add(info.Name);
                        continue;
                    }
                    // 保留两者：走下面的新增流程
                }
                var item = new EncryptedFile
                {
                    ProjectName = group,
                    Name = info.Name,
                    OriginalName = info.Name,
                    Size = info.Length,
                    CreatedTime = now,
                    UpdatedTime = now
                };
                DataStore.SaveEncryptedFileContent(item.Id, data, _password);
                added.Add(item);
                _allFiles.Add(item);
                knownNames[item.Name] = item;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}：{ex.Message}");
            }
        }

        if (added.Count > 0 || overwritten.Count > 0)
        {
            try
            {
                DataStore.SaveEncryptedFiles(_allFiles, _password);
            }
            catch (Exception ex)
            {
                foreach (var item in added)
                {
                    _allFiles.Remove(item);
                    DataStore.DeleteEncryptedFileContent(item.Id);
                }
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        RefreshEncryptedFiles();
        var summary = new List<string>();
        if (added.Count > 0) summary.Add($"已加密保存 {added.Count} 个文件");
        if (overwritten.Count > 0) summary.Add($"覆盖 {overwritten.Count} 个文件");
        if (skipped.Count > 0) summary.Add($"跳过 {skipped.Count} 个文件");
        if (errors.Count > 0)
        {
            summary.Add("以下文件未导入：\n" + string.Join("\n", errors));
            MessageBox.Show(string.Join("\n", summary), "导入",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (summary.Count > 0)
        {
            MessageBox.Show(string.Join("，", summary) + "。", "导入",
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

    private static ListBoxItem? FindListBoxItem(DependencyObject? src)
    {
        DependencyObject? d = src;
        int guard = 0;
        while (d != null && d is not ListBoxItem && guard++ < 20)
            d = VisualTreeHelper.GetParent(d);
        return d as ListBoxItem;
    }

    private void MenuExportEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
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

    // 把选中的加密文件移动到其它分组
    private void MenuMoveEncryptedFileGroup_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要修改分组的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gdlg = new GroupPickerDialog("修改分组", _groups, GroupKey(item.ProjectName), CountsByGroup(), _monitor) { Owner = this };
        if (gdlg.ShowDialog() != true) return;
        ApplyGroupDialog(gdlg);
        string group = _currentGroup;
        if (GroupKey(item.ProjectName) == group) return;

        item.ProjectName = group;
        try
        {
            DataStore.SaveEncryptedFiles(_allFiles, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshEncryptedFiles();
    }

    private void MenuRenameEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
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
            DataStore.SaveEncryptedFiles(_allFiles, _password);
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
        _monitor?.NotifyActivity();
        if (LstEncryptedFiles.SelectedItem is not EncryptedFile item)
        {
            MessageBox.Show("请先右键点选要删除的文件。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除加密文件「{item.DisplayName}」吗？\n删除后无法恢复（建议先导出解密副本备份）。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        int index = _allFiles.IndexOf(item);
        _allFiles.Remove(item);
        try
        {
            DataStore.SaveEncryptedFiles(_allFiles, _password);
        }
        catch (Exception ex)
        {
            _allFiles.Insert(Math.Min(index, _allFiles.Count), item);
            MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DataStore.DeleteEncryptedFileContent(item.Id);
        RefreshEncryptedFiles();
    }

    private void EncryptedFilesMenu_Opened(object sender, RoutedEventArgs e)
    {
        MenuEditEncryptedFile.IsEnabled = LstEncryptedFiles.SelectedItem is EncryptedFile item && item.CanEdit;
    }

    private void MenuEditEncryptedFile_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
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
            _monitor?.NotifyActivity();
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

        if (_openEditors.TryGetValue(item.Id, out var opened))
        {
            if (opened.WindowState == WindowState.Minimized)
                opened.WindowState = WindowState.Normal;
            opened.Activate();
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
            var editor = new CodeEditorWindow(item, data, _password, _monitor, OnEditorSaved);
            _openEditors[item.Id] = editor;
            editor.Closed += (_, _) =>
            {
                _openEditors.Remove(item.Id);
                if (IsLoaded) RefreshEncryptedFiles();
            };
            editor.Show();
        }
        catch (Exception ex)
        {
            _openEditors.Remove(item.Id);
            App.ReportFatal("在线编辑", ex);
            return;
        }
    }

    private void OnEditorSaved(EncryptedFile item)
    {
        try
        {
            var files = DataStore.LoadEncryptedFiles(_password);
            var current = files.FirstOrDefault(x => x.Id == item.Id);
            if (current != null)
            {
                current.Size = item.Size;
                current.UpdatedTime = item.UpdatedTime;
            }
            DataStore.SaveEncryptedFiles(files, _password);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"文件内容已加密保存，但文件列表信息保存失败：{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (IsLoaded) RefreshEncryptedFiles();
    }
}
