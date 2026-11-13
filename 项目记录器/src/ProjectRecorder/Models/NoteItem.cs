using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace ProjectRecorder.Models;

/// <summary>
/// 笔记：标题 + 正文，通用模块独立功能，加密存 Data/notes.dat。
/// 实现 INotifyPropertyChanged 以便编辑时列表卡片标题/时间实时刷新（不重设 ItemsSource，避免打断输入）。
/// </summary>
[DataContract]
public class NoteItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _content = string.Empty;
    private DateTime _updatedTime = DateTime.Now;

    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();

    [DataMember]
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            Raise(nameof(Title));
            Raise(nameof(DisplayTitle));
        }
    }

    [DataMember]
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            Raise(nameof(Content));
        }
    }

    [DataMember] public DateTime CreatedTime { get; set; } = DateTime.Now;

    [DataMember]
    public DateTime UpdatedTime
    {
        get => _updatedTime;
        set
        {
            if (_updatedTime == value) return;
            _updatedTime = value;
            Raise(nameof(UpdatedTime));
        }
    }

    [IgnoreDataMember]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "未命名笔记" : Title.Trim();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
