using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProjectRecorder.Helpers;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 通用模块「笔记」视图：列表 + 标题/正文编辑，700ms 防抖自动加密保存。
/// 通过 Initialize 注入主窗口的数据门面与无操作监控；切页时由主窗口调用 SaveNow。
/// </summary>
public partial class NoteView : UserControl
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly List<NoteItem> _notes = new();
    private DataService? _data;
    private InactivityMonitor? _monitor;
    private NoteItem? _currentNote;
    private bool _loading;
    private bool _dirty;

    public NoteView()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>进入笔记页：先落盘上一篇，再重新加载列表并保持当前选中。</summary>
    public void Reload()
    {
        if (_data == null) return;
        SaveNow();

        string? keepId = _currentNote?.Id;
        _notes.Clear();
        _notes.AddRange(_data.LoadNotes() ?? new List<NoteItem>());
        RefreshList(keepId);
    }

    /// <summary>立即保存未落盘的改动（切页 / 切笔记 / 退出时由 MainWindow 调用）。</summary>
    public void SaveNow()
    {
        if (!_dirty || _currentNote == null || _data == null) return;

        _saveTimer.Stop();
        _currentNote.UpdatedTime = DateTime.Now;
        if (!_data.SaveNotes(_notes)) return;
        _dirty = false;
        TxtNoteStatus.Text = $"已自动保存 {DateTime.Now:HH:mm:ss}";
    }

    private void RefreshList(string? keepId = null)
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
            ShowNoEditor();
        }
    }

    private void LstNotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LstNotes.SelectedItem is not NoteItem note) return;
        if (ReferenceEquals(note, _currentNote)) return;

        SaveNow();
        LoadIntoEditor(note);
    }

    private void LoadIntoEditor(NoteItem note)
    {
        _currentNote = note;
        _loading = true;
        try
        {
            TxtNoteTitle.Text = note.Title ?? string.Empty;
            TxtNoteBody.Text = note.Content ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }

        TxtNoteTitle.IsEnabled = true;
        TxtNoteBody.IsEnabled = true;
        TxtNoteNoSel.Visibility = Visibility.Collapsed;
        TxtNoteStatus.Text = string.Empty;
    }

    private void ShowNoEditor()
    {
        _loading = true;
        try
        {
            TxtNoteTitle.Text = string.Empty;
            TxtNoteBody.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }

        TxtNoteTitle.IsEnabled = false;
        TxtNoteBody.IsEnabled = false;
        TxtNoteNoSel.Visibility = Visibility.Visible;
        TxtNoteStatus.Text = string.Empty;
    }

    // 输入即更新内存模型（列表卡片标题/时间实时刷新），磁盘保存用 700ms 防抖
    private void NoteText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _currentNote == null) return;

        _currentNote.Title = TxtNoteTitle.Text;
        _currentNote.Content = TxtNoteBody.Text;
        _dirty = true;
        TxtNoteStatus.Text = "编辑中…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void BtnAddNote_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        SaveNow();

        var note = new NoteItem { Title = "新笔记", Content = string.Empty };
        _notes.Insert(0, note);
        if (_data == null || !_data.SaveNotes(_notes))
        {
            _notes.Remove(note);
            return;
        }

        RefreshList(note.Id);
        TxtNoteTitle.Focus();
        TxtNoteTitle.SelectAll();
    }

    private void LstNotes_RightButtonDown(object sender, MouseButtonEventArgs e)
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

    private void MenuDeleteNote_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (LstNotes.SelectedItem is not NoteItem note)
        {
            MessageBox.Show("请先点选要删除的笔记。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show($"确定删除笔记「{note.DisplayTitle}」吗？", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;

        _saveTimer.Stop();
        _dirty = false;
        int index = _notes.IndexOf(note);
        _notes.Remove(note);
        if (_data == null || !_data.SaveNotes(_notes, "删除失败"))
        {
            _notes.Insert(Math.Min(index, _notes.Count), note);
            return;
        }

        _currentNote = null;
        RefreshList();
    }
}
