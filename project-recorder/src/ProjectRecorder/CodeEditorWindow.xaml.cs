using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using Markdig;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 加密文件在线编辑窗口（仅 .md/.sh/.py）：AvalonEdit 语法高亮 + 行号，深色 UI + VS Code Dark+ 配色。
/// 保存时直接 AES-256 重新加密写回 Data/files/&lt;id&gt;.enc，绝不明文落盘；弹窗内操作同样上报无操作计时。
/// </summary>
public partial class CodeEditorWindow : Window
{
    private static bool _customHighlightingsReady;

    private readonly EncryptedFile _file;
    private readonly string _password;
    private readonly Action<EncryptedFile>? _onSaved;
    private readonly Encoding _encoding;
    private readonly int _preambleLength;
    private readonly string _language;
    private readonly string _languageDisplay;
    private bool _dirty;
    /// <summary>上次渲染预览时的文本：无变化时跳过重渲染，避免滚动条重置和闪烁。</summary>
    private string? _lastPreviewText;
    /// <summary>刷新预览前记下的滚动位置，页面加载完成后恢复。</summary>
    private double _previewScrollY;
    /// <summary>编辑停顿后自动保存的防抖计时器。</summary>
    private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private const double DefaultFontSize = 14;
    private const double MinFontSize = 10;
    private const double MaxFontSize = 28;
    /// <summary>选中重复值高亮渲染器。</summary>
    private SegmentHighlightRenderer? _occurrenceRenderer;
    /// <summary>查找命中高亮渲染器（橙色）。</summary>
    private readonly SegmentHighlightRenderer _findRenderer =
        new(new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0x9E, 0x0B)));
    private int _findIndex = -1;
    private bool _suppressHighlightRefresh;
    private int _docHitCount;
    private int _docHitIndex = -1;
    private bool _docSectionUpdating;

    private static MarkdownPipeline? _mdPipeline;
    private static bool _mdInitTried;

    /// <summary>Markdown 管线延迟初始化：内嵌 Markdig（或其依赖）缺失时返回 null，预览自动降级，绝不影响编辑。</summary>
    private static MarkdownPipeline? GetMdPipeline()
    {
        if (_mdPipeline != null) return _mdPipeline;
        if (_mdInitTried) return null;
        _mdInitTried = true;
        try
        {
            _mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        }
        catch
        {
            _mdPipeline = null;
        }
        return _mdPipeline;
    }

    public CodeEditorWindow(EncryptedFile file, byte[] data, string password,
        InactivityMonitor? monitor = null, Action<EncryptedFile>? onSaved = null)
    {
        InitializeComponent();
        _file = file;
        _password = password;
        _onSaved = onSaved;
        _language = file.EditorLanguage;
        _languageDisplay = DisplayLanguage(_language);

        EnsureCustomHighlightings();
        Decode(data ?? Array.Empty<byte>(), out string text, out _encoding, out _preambleLength);

        Title = $"在线编辑 - {file.DisplayName}";
        TxtFileName.Text = file.DisplayName;
        TxtFileInfo.Text = $"加密存储 · {file.SizeText} · {_languageDisplay}";

        Editor.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Editor.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        Editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
        Editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.FromRgb(0xAE, 0xAF, 0xAD));
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x4F, 0x78));
        Editor.TextArea.SelectionForeground = new SolidColorBrush(Colors.White);

        ApplySyntaxHighlighting();
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(0x2F, 0x33, 0x37));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x33, 0x37)), 1);
        SetupEditorComfort();
        Editor.Text = text;
        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursor();
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            AutoSave();
            RefreshPreview();
        };
        // 预览 / 资料里的外部链接不跳转（避免被导航走）
        PreviewBrowser.Navigating += (s, ev) => CancelExternalNavigation(ev);
        DocBrowser.Navigating += (s, ev) => CancelExternalNavigation(ev);
        // 资料页加载完成后，若已有搜索词则重跑一次搜索
        DocBrowser.LoadCompleted += (_, _) =>
        {
            if (!string.IsNullOrEmpty(TxtDocSearch.Text)) RunDocSearch();
        };
        // 刷新后恢复之前的滚动位置（避免每次编辑预览都跳回顶部/底部）
        PreviewBrowser.LoadCompleted += (_, _) =>
        {
            try
            {
                if (_previewScrollY > 0)
                    PreviewBrowser.InvokeScript("eval",
                        $"window.scrollTo(0, {_previewScrollY.ToString(CultureInfo.InvariantCulture)});");
            }
            catch
            {
                // 脚本恢复失败不影响编辑
            }
        };

        bool isMarkdown = _language == "MarkDown";
        bool previewOk = isMarkdown && GetMdPipeline() != null;
        ChkPreview.Visibility = previewOk ? Visibility.Visible : Visibility.Collapsed;
        if (isMarkdown && !previewOk)
            ChkPreview.ToolTip = "预览组件加载失败，仅编辑可用";
        if (previewOk) ChkPreview.IsChecked = true; // md 默认显示预览
        UpdateCursor();
        UpdateMeta();

        HookActivity(monitor);
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.CaretOffset = 0;
            UpdateOverflowHint();
        };
    }

    private void HookActivity(InactivityMonitor? monitor)
    {
        if (monitor == null) return;
        PreviewMouseMove += (s, e) => monitor.NotifyActivity();
        PreviewMouseDown += (s, e) => monitor.NotifyActivity();
        PreviewMouseUp += (s, e) => monitor.NotifyActivity();
        PreviewMouseWheel += (s, e) => monitor.NotifyActivity();
        PreviewKeyDown += (s, e) => monitor.NotifyActivity();
        PreviewKeyUp += (s, e) => monitor.NotifyActivity();
        PreviewTextInput += (s, e) => monitor.NotifyActivity();
    }

    // ---------- 编码增强：缩进策略 / 查找 / 字号缩放 / 行注释开关 ----------

    private void SetupEditorComfort()
    {
        // 软制表符（4 空格）+ 回车自动缩进，Tab/Shift+Tab 整块缩进
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.TextArea.IndentationStrategy = new DefaultIndentationStrategy();
        Editor.Options.EnableRectangularSelection = true;
        Editor.Options.EnableTextDragDrop = true;
        Editor.Options.AllowScrollBelowDocument = true;

        // 选中重复值高亮 + 查找命中高亮
        _occurrenceRenderer = new SegmentHighlightRenderer(
            new SolidColorBrush(Color.FromArgb(0x45, 0x25, 0x63, 0xEB)));
        Editor.TextArea.TextView.BackgroundRenderers.Add(_occurrenceRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_findRenderer);
        Editor.TextArea.SelectionChanged += (_, _) => UpdateOccurrenceHighlights();

        // 右侧简化缩略条
        Overview.Attach(Editor);

        // 内容超宽提示：视口尺寸变化、可视行重排（滚动/编辑/字号）时重新判断
        Editor.SizeChanged += (_, _) => UpdateOverflowHint();
        Editor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateOverflowHint();

        Editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
        UpdateFontSizeText();
    }

    /// <summary>选中文本变化时重算重复值位置：单行、长度 2~64 的选区才高亮，避免噪声与卡顿。</summary>
    private void UpdateOccurrenceHighlights()
    {
        if (_occurrenceRenderer == null) return;
        var segments = _occurrenceRenderer.Segments;
        segments.Clear();
        var lines = new HashSet<int>();
        int matchCount = 0;

        string selected = Editor.SelectedText;
        if (selected.Length >= 2 && selected.Length <= 64
            && !string.IsNullOrWhiteSpace(selected)
            && selected.IndexOfAny(new[] { '\r', '\n' }) < 0
            && Editor.Document.TextLength <= 200_000) // 超大文件不高亮，避免卡顿
        {
            string text = Editor.Document.Text;
            int index = 0;
            while ((index = text.IndexOf(selected, index, StringComparison.Ordinal)) >= 0)
            {
                matchCount++;
                lines.Add(Editor.Document.GetLineByOffset(index).LineNumber); // 供右侧缩略条标记
                if (index != Editor.SelectionStart) // 选区自身由系统高亮呈现
                    segments.Add(new TextSegment { StartOffset = index, Length = selected.Length });
                index += selected.Length;
                if (matchCount >= 500) break; // 上限，防止大文档卡顿
            }
        }

        Overview.SetHighlightLines(lines);
        Editor.TextArea.TextView.InvalidateVisual();
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetFontSize(Editor.FontSize + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
    }

    private void SetFontSize(double size)
    {
        size = Math.Max(MinFontSize, Math.Min(MaxFontSize, size));
        if (Math.Abs(size - Editor.FontSize) < 0.01) return;
        Editor.FontSize = size;
        UpdateFontSizeText();
        UpdateOverflowHint();
    }

    private void UpdateFontSizeText()
    {
        if (TxtZoom == null) return;
        TxtZoom.Text = $"字号 {Editor.FontSize:0}";
    }

    /// <summary>代码宽度超过可视区域（需要横向滚动）时，在状态栏给出提示。</summary>
    private void UpdateOverflowHint()
    {
        if (TxtOverflow == null) return;
        bool overflow = Editor.ExtentWidth > Editor.ViewportWidth + 0.5;
        TxtOverflow.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 查找 / 替换 ----------

    private void OpenFind(bool withReplace)
    {
        FindPanel.Visibility = Visibility.Visible;
        string sel = Editor.SelectedText;
        if (sel.Length > 0 && sel.IndexOfAny(new[] { '\r', '\n' }) < 0)
            TxtFind.Text = sel;
        UpdateFindMatches();
        if (withReplace)
        {
            TxtReplace.Focus();
            TxtReplace.SelectAll();
        }
        else
        {
            TxtFind.Focus();
            TxtFind.SelectAll();
        }
    }

    private void CloseFind()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        _findRenderer.Segments.Clear();
        _findIndex = -1;
        Editor.TextArea.TextView.InvalidateVisual();
        Editor.Focus();
    }

    private void TxtFind_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindPanel.Visibility != Visibility.Visible) return;
        UpdateFindMatches();
    }

    private void FindOption_Changed(object sender, RoutedEventArgs e)
    {
        if (FindPanel.Visibility != Visibility.Visible) return;
        UpdateFindMatches();
    }

    private void TxtFind_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrev();
            else FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void TxtReplace_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ReplaceOne(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseFind(); e.Handled = true; }
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindPrev();
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();
    private void ReplaceOne_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    /// <summary>
    /// 按当前条件重新计算全部命中。select=true 时把当前项定位到光标之后；
    /// select=false 用于编辑内容变化时仅刷新高亮、不移动光标。
    /// </summary>
    private void UpdateFindMatches(bool select = true)
    {
        var segs = _findRenderer.Segments;
        segs.Clear();
        _findIndex = -1;

        string query = TxtFind.Text;
        if (query.Length > 0 && Editor.Document.TextLength <= 2_000_000)
        {
            string text = Editor.Text;
            if (ChkRegex.IsChecked == true)
            {
                try
                {
                    var opts = RegexOptions.Multiline;
                    if (ChkCase.IsChecked != true) opts |= RegexOptions.IgnoreCase;
                    foreach (Match m in Regex.Matches(text, query, opts))
                    {
                        if (m.Length == 0) continue;
                        if (ChkWhole.IsChecked == true && !IsWholeWord(text, m.Index, m.Length)) continue;
                        segs.Add(new TextSegment { StartOffset = m.Index, Length = m.Length });
                        if (segs.Count >= 5000) break;
                    }
                }
                catch (ArgumentException)
                {
                    // 正则语法错误：视为无结果
                }
            }
            else
            {
                var cmp = ChkCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                int index = 0;
                while ((index = text.IndexOf(query, index, cmp)) >= 0)
                {
                    if (ChkWhole.IsChecked != true || IsWholeWord(text, index, query.Length))
                    {
                        segs.Add(new TextSegment { StartOffset = index, Length = query.Length });
                        if (segs.Count >= 5000) break;
                    }
                    index += query.Length;
                }
            }
        }

        if (segs.Count > 0)
        {
            int caret = Editor.SelectionStart;
            _findIndex = segs.FindIndex(s => s.Offset >= caret);
            if (_findIndex < 0) _findIndex = 0;
            if (select)
            {
                SelectFindMatch();
                return;
            }
        }
        UpdateFindResult();
        Editor.TextArea.TextView.InvalidateVisual();
    }

    private void FindNext()
    {
        int n = _findRenderer.Segments.Count;
        if (n == 0) { UpdateFindMatches(); return; }
        _findIndex = (_findIndex + 1) % n;
        SelectFindMatch();
    }

    private void FindPrev()
    {
        int n = _findRenderer.Segments.Count;
        if (n == 0) { UpdateFindMatches(); return; }
        _findIndex = (_findIndex - 1 + n) % n;
        SelectFindMatch();
    }

    private void SelectFindMatch()
    {
        if (_findIndex < 0 || _findIndex >= _findRenderer.Segments.Count) return;
        var seg = _findRenderer.Segments[_findIndex];
        Editor.Select(seg.Offset, seg.Length);
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(seg.Offset).LineNumber);
        UpdateFindResult();
        Editor.TextArea.TextView.InvalidateVisual();
    }

    private void UpdateFindResult()
    {
        if (TxtFindResult == null) return;
        int n = _findRenderer.Segments.Count;
        TxtFindResult.Text = TxtFind.Text.Length == 0 ? string.Empty
            : n == 0 ? "无结果"
            : $"{_findIndex + 1}/{n}";
    }

    private void ReplaceOne()
    {
        int n = _findRenderer.Segments.Count;
        if (n == 0 || _findIndex < 0 || _findIndex >= n) { UpdateFindMatches(); return; }
        var seg = _findRenderer.Segments[_findIndex];
        _suppressHighlightRefresh = true;
        try
        {
            Editor.Document.Replace(seg.Offset, seg.Length, TxtReplace.Text);
        }
        finally
        {
            _suppressHighlightRefresh = false;
        }
        UpdateFindMatches();
    }

    private void ReplaceAll()
    {
        var segs = _findRenderer.Segments;
        if (segs.Count == 0) return;
        string rep = TxtReplace.Text;
        var ranges = new List<(int Offset, int Length)>(segs.Count);
        foreach (var s in segs) ranges.Add((s.Offset, s.Length));

        var doc = Editor.Document;
        _suppressHighlightRefresh = true;
        doc.BeginUpdate();
        try
        {
            // 从后往前替换，避免前面替换影响后面偏移
            for (int i = ranges.Count - 1; i >= 0; i--)
                doc.Replace(ranges[i].Offset, ranges[i].Length, rep);
        }
        finally
        {
            doc.EndUpdate();
            _suppressHighlightRefresh = false;
        }
        UpdateFindMatches();
    }

    private static bool IsWholeWord(string text, int offset, int length)
    {
        bool leftOk = offset == 0 || !IsWordChar(text[offset - 1]);
        bool rightOk = offset + length >= text.Length || !IsWordChar(text[offset + length]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt 组合键在 WPF 里真实按键落在 SystemKey 上
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && FindPanel.Visibility == Visibility.Visible)
        {
            CloseFind();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (key)
        {
            case Key.F:             // Ctrl+F 查找
                OpenFind(false);
                e.Handled = true;
                break;
            case Key.H:             // Ctrl+H 查找并替换
                OpenFind(true);
                e.Handled = true;
                break;
            case Key.OemQuestion:   // Ctrl+/
            case Key.Divide:
                ToggleComment();
                e.Handled = true;
                break;
            case Key.D0:            // Ctrl+0 复位字号
            case Key.NumPad0:
                SetFontSize(DefaultFontSize);
                e.Handled = true;
                break;
            case Key.OemPlus:       // Ctrl+(Shift+=) 放大
            case Key.Add:
                SetFontSize(Editor.FontSize + 1);
                e.Handled = true;
                break;
            case Key.OemMinus:      // Ctrl+- 缩小
            case Key.Subtract:
                SetFontSize(Editor.FontSize - 1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>行注释开关：Python / Shell 用 “# ”，Markdown 无行注释直接忽略。</summary>
    private void ToggleComment()
    {
        if (_language != "Python" && _language != "Bash") return;
        var doc = Editor.Document;
        if (doc == null) return;

        int selStart = Editor.SelectionStart;
        int selEnd = selStart + Editor.SelectionLength;
        int firstLine = doc.GetLineByOffset(selStart).LineNumber;
        int lastLine = doc.GetLineByOffset(selEnd).LineNumber;

        bool allCommented = true;
        for (int n = firstLine; n <= lastLine; n++)
        {
            if (!doc.GetText(doc.GetLineByNumber(n)).TrimStart().StartsWith("#"))
            {
                allCommented = false;
                break;
            }
        }

        doc.BeginUpdate();
        try
        {
            // 自下而上处理，避免前一行插入/删除影响后一行偏移
            for (int n = lastLine; n >= firstLine; n--)
            {
                var line = doc.GetLineByNumber(n);
                if (allCommented)
                {
                    string text = doc.GetText(line);
                    int indent = 0;
                    while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t')) indent++;
                    int remove = indent + 1 < text.Length && text[indent + 1] == ' ' ? 2 : 1;
                    doc.Remove(line.Offset + indent, remove);
                }
                else
                {
                    doc.Insert(line.Offset, "# ");
                }
            }
        }
        finally
        {
            doc.EndUpdate();
        }
    }

    // ---------- 语法高亮：Bash/PythonDark 用自带 Dark+ 定义；内置 MarkDown 染色；Python 定义丢失时给内置染色兜底 ----------

    private static void EnsureCustomHighlightings()
    {
        if (_customHighlightingsReady) return;
        _customHighlightingsReady = true;
        LoadHighlighting("ProjectRecorder.Resources.Bash.xshd", "Bash", new[] { ".sh", ".bash" });
        LoadHighlighting("ProjectRecorder.Resources.Python.xshd", "PythonDark", new[] { ".py", ".pyw" });
    }

    private static void LoadHighlighting(string resourceName, string registerName, string[] extensions)
    {
        try
        {
            using var stream = typeof(CodeEditorWindow).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;
            using var reader = XmlReader.Create(stream);
            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(registerName, extensions, definition);
        }
        catch
        {
            // 高亮定义加载失败不影响编辑功能
        }
    }

    private void ApplySyntaxHighlighting()
    {
        if (_language == "Python")
        {
            var python = HighlightingManager.Instance.GetDefinition("PythonDark");
            if (python == null)
            {
                // 自定义定义没加载成功：退回内置 Python 并染成 Dark+ 色
                python = HighlightingManager.Instance.GetDefinition("Python");
                if (python != null) TintPythonDark(python);
            }
            Editor.SyntaxHighlighting = python;
            return;
        }

        var definition = HighlightingManager.Instance.GetDefinition(_language);
        if (definition != null && _language == "MarkDown") TintMarkdownDark(definition);
        Editor.SyntaxHighlighting = definition;
    }

    /// <summary>内置 Python 定义的 Dark+ 兜底配色（单 Keywords 桶取蓝色，最接近 VS Code 主体观感）。</summary>
    private static void TintPythonDark(IHighlightingDefinition definition)
    {
        Tint(definition, "Comment", "#6A9955");
        Tint(definition, "String", "#CE9178");
        Tint(definition, "MethodCall", "#DCDCAA");
        Tint(definition, "NumberLiteral", "#B5CEA8");
        Tint(definition, "Keywords", "#569CD6", bold: false);
    }

    /// <summary>内置 MarkDown 定义的 Dark+ 配色（无深色内置定义，只能运行时染色）。</summary>
    private static void TintMarkdownDark(IHighlightingDefinition definition)
    {
        Tint(definition, "Heading", "#569CD6", bold: true);
        Tint(definition, "StrongEmphasis", "#569CD6");
        Tint(definition, "Emphasis", "#C586C0");
        Tint(definition, "Code", "#CE9178");
        Tint(definition, "BlockQuote", "#6A9955");
        Tint(definition, "Link", "#569CD6");
        Tint(definition, "Image", "#DCDCAA");
        ClearBackground(definition, "LineBreak");
    }

    private static void Tint(IHighlightingDefinition definition, string colorName, string hex, bool? bold = null)
    {
        foreach (var color in definition.NamedHighlightingColors)
        {
            if (!string.Equals(color.Name, colorName, StringComparison.Ordinal)) continue;
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
                color.Foreground = new SimpleHighlightingBrush(parsed);
            if (bold.HasValue)
                color.FontWeight = bold.Value ? FontWeights.Bold : (FontWeight?)null;
            return;
        }
    }

    private static void ClearBackground(IHighlightingDefinition definition, string colorName)
    {
        foreach (var color in definition.NamedHighlightingColors)
        {
            if (string.Equals(color.Name, colorName, StringComparison.Ordinal))
            {
                color.Background = new SimpleHighlightingBrush(Color.FromArgb(0, 0, 0, 0));
                return;
            }
        }
    }

    private static string DisplayLanguage(string language) => language switch
    {
        "MarkDown" => "Markdown",
        "Python" => "Python",
        "Bash" => "Shell",
        _ => language
    };

    // ---------- 编码：保留原 BOM；无 BOM 时优先 UTF-8，失败退回 GB18030（中文 Windows 旧文件） ----------

    private static void Decode(byte[] data, out string text, out Encoding encoding, out int preambleLength)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            preambleLength = 3;
        }
        else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            preambleLength = 2;
        }
        else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            preambleLength = 2;
        }
        else
        {
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
                encoding = new UTF8Encoding(false);
                preambleLength = 0;
                return;
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    encoding = Encoding.GetEncoding("GB18030");
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }
                preambleLength = 0;
            }
        }

        text = encoding.GetString(data, preambleLength, data.Length - preambleLength);
    }

    private byte[] EncodeCurrentText()
    {
        byte[] body = _encoding.GetBytes(Editor.Text);
        if (_preambleLength <= 0) return body;

        byte[] preamble = _encoding.GetPreamble();
        byte[] bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    // ---------- 保存 / 关闭 ----------

    // 编辑停顿约 1 秒后自动保存（也能覆盖 120 秒无操作强制退出前的场景）
    private void AutoSave()
    {
        if (_dirty) SaveNow(auto: true);
    }

    private bool SaveNow(bool auto = false)
    {
        try
        {
            byte[] bytes = EncodeCurrentText();
            DataStore.SaveEncryptedFileContent(_file.Id, bytes, _password);
            _file.Size = bytes.Length;
            _file.UpdatedTime = DateTime.Now;
            _onSaved?.Invoke(_file);
            _dirty = false;
            TxtFileInfo.Text = $"加密存储 · {_file.SizeText} · {_languageDisplay} · {(auto ? "自动保存" : "已保存")} {DateTime.Now:HH:mm:ss}";
            Title = $"在线编辑 - {_file.DisplayName}";
            UpdateMeta();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveNow();

    private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

    private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SaveNow();
        e.Handled = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        if (!_dirty) return;

        var result = MessageBox.Show(this, "内容尚未保存，是否保存后再关闭？", "关闭确认",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (result == MessageBoxResult.Yes && !SaveNow())
            e.Cancel = true;
    }

    // ---------- Markdown 预览（仅 .md，右侧分栏，编辑停顿后自动刷新） ----------

    private void ChkPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (Editor == null) return;
        if (ChkPreview.IsChecked == true)
        {
            _lastPreviewText = null; // 面板刚显示时强制渲染一次（浏览器可能是空白页）
            RefreshPreview();
        }
        UpdateRightPane();
    }

    // ---------- 资料面板（sh / py / md 中文速查） ----------

    /// <summary>右侧窄栏：资料面板优先，其次 Markdown 预览；两者共用一个列。</summary>
    private void UpdateRightPane()
    {
        bool docs = ChkDocs.IsChecked == true;
        bool preview = !docs && ChkPreview.IsChecked == true && ChkPreview.Visibility == Visibility.Visible;
        DocPane.Visibility = docs ? Visibility.Visible : Visibility.Collapsed;
        PreviewPane.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        bool any = docs || preview;
        PreviewSplitter.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitterCol.Width = any ? new GridLength(4) : new GridLength(0);
        PreviewCol.Width = any ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void ChkDocs_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkDocs.IsChecked == true)
        {
            SelectDocForLanguage();
            LoadCurrentDoc();
        }
        UpdateRightPane();
    }

    private void CmbDocLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocPane == null || DocPane.Visibility != Visibility.Visible) return;
        LoadCurrentDoc();
    }

    private void SelectDocForLanguage()
    {
        int index = _language switch { "Bash" => 0, "Python" => 1, "MarkDown" => 2, _ => 0 };
        if (CmbDocLang.SelectedIndex != index) CmbDocLang.SelectedIndex = index;
    }

    private void LoadCurrentDoc()
    {
        string lang = (CmbDocLang.SelectedItem as ComboBoxItem)?.Content as string ?? "Shell";
        string md = LoadDocMarkdown(lang);
        PopulateDocSections(md);
        _docHitCount = 0;
        _docHitIndex = -1;
        UpdateDocSearchResult();
        try
        {
            DocBrowser.NavigateToString(BuildHtml(md, DocCss));
        }
        catch
        {
            // 资料加载失败不影响编辑
        }
    }

    /// <summary>用文档里的二级标题（##）填充“跳转章节”下拉。</summary>
    private void PopulateDocSections(string markdown)
    {
        _docSectionUpdating = true;
        try
        {
            CmbDocSection.Items.Clear();
            foreach (var raw in markdown.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("## "))
                    CmbDocSection.Items.Add(line.Substring(3).Trim());
            }
            CmbDocSection.SelectedIndex = -1;
        }
        finally
        {
            _docSectionUpdating = false;
        }
    }

    private void CmbDocSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_docSectionUpdating) return;
        int idx = CmbDocSection.SelectedIndex;
        if (idx < 0) return;
        try
        {
            DocBrowser.InvokeScript("eval",
                $"var h=document.getElementsByTagName('h2'); if(h[{idx}]) h[{idx}].scrollIntoView(true);");
        }
        catch
        {
            // 跳转失败忽略
        }
    }

    private void TxtDocSearch_TextChanged(object sender, TextChangedEventArgs e) => RunDocSearch();

    private void TxtDocSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) DocPrev();
            else DocNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TxtDocSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void DocPrev_Click(object sender, RoutedEventArgs e) => DocPrev();
    private void DocNext_Click(object sender, RoutedEventArgs e) => DocNext();

    private void RunDocSearch()
    {
        try
        {
            object r = DocBrowser.InvokeScript("docFindAll", TxtDocSearch.Text ?? string.Empty, false);
            _docHitCount = r == null ? 0 : Convert.ToInt32(r);
            _docHitIndex = _docHitCount > 0 ? 0 : -1;
            if (_docHitIndex >= 0) DocBrowser.InvokeScript("docGoHit", 0);
        }
        catch
        {
            _docHitCount = 0;
            _docHitIndex = -1;
        }
        UpdateDocSearchResult();
    }

    private void DocNext()
    {
        if (_docHitCount == 0) { RunDocSearch(); return; }
        _docHitIndex = (_docHitIndex + 1) % _docHitCount;
        try { DocBrowser.InvokeScript("docGoHit", _docHitIndex); } catch { }
        UpdateDocSearchResult();
    }

    private void DocPrev()
    {
        if (_docHitCount == 0) { RunDocSearch(); return; }
        _docHitIndex = (_docHitIndex - 1 + _docHitCount) % _docHitCount;
        try { DocBrowser.InvokeScript("docGoHit", _docHitIndex); } catch { }
        UpdateDocSearchResult();
    }

    private void UpdateDocSearchResult()
    {
        if (TxtDocSearchResult == null) return;
        if (string.IsNullOrEmpty(TxtDocSearch.Text)) TxtDocSearchResult.Text = string.Empty;
        else if (_docHitCount == 0) TxtDocSearchResult.Text = "无";
        else TxtDocSearchResult.Text = $"{_docHitIndex + 1}/{_docHitCount}";
    }

    private static string LoadDocMarkdown(string lang)
    {
        string name = lang switch
        {
            "Python" => "ProjectRecorder.Resources.ref-py.md",
            "Markdown" => "ProjectRecorder.Resources.ref-md.md",
            _ => "ProjectRecorder.Resources.ref-sh.md"
        };
        try
        {
            using var stream = typeof(CodeEditorWindow).Assembly.GetManifestResourceStream(name);
            if (stream == null) return "（未找到文档资源：" + name + "）";
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return "（文档加载失败：" + ex.Message + "）";
        }
    }

    private static void CancelExternalNavigation(System.Windows.Navigation.NavigatingCancelEventArgs ev)
    {
        if (ev.Uri != null && (ev.Uri.Scheme == "http" || ev.Uri.Scheme == "https" || ev.Uri.Scheme == "file"))
            ev.Cancel = true;
    }

    private void RefreshPreview()
    {
        if (PreviewPane == null || PreviewPane.Visibility != Visibility.Visible) return;
        if (string.Equals(Editor.Text, _lastPreviewText, StringComparison.Ordinal)) return; // 内容没变不重渲染
        try
        {
            _previewScrollY = GetPreviewScrollY();
            _lastPreviewText = Editor.Text;
            PreviewBrowser.NavigateToString(BuildMarkdownHtml(Editor.Text));
        }
        catch
        {
            // 预览失败不影响编辑
        }
    }

    private double GetPreviewScrollY()
    {
        try
        {
            object? r = PreviewBrowser.InvokeScript("eval",
                "document.documentElement ? (document.documentElement.scrollTop||0) : (document.body ? (document.body.scrollTop||0) : 0)");
            if (r != null && double.TryParse(r.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                return Math.Max(0, y);
        }
        catch
        {
            // 文档还没加载好时取不到位置，按 0 处理
        }
        return 0;
    }

    private static string BuildMarkdownHtml(string markdown) => BuildHtml(markdown, PreviewCss);

    private static string BuildHtml(string markdown, string css)
    {
        string body;
        var pipeline = GetMdPipeline();
        try
        {
            body = pipeline == null
                ? System.Net.WebUtility.HtmlEncode(markdown ?? string.Empty)
                : Markdown.ToHtml(markdown ?? string.Empty, pipeline);
        }
        catch
        {
            body = System.Net.WebUtility.HtmlEncode(markdown ?? string.Empty);
        }
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
               "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">" +
               "<style>" + css + "</style></head><body>" + body + DocFindScript + "</body></html>";
    }

    /// <summary>资料页内查找脚本：收集命中文本节点，逐个高亮并滚动定位。</summary>
    private const string DocFindScript = @"
<script type=""text/javascript"">
window.__hitNodes=[]; window.__hitIdx=-1; window.__hitLen=0;
function docFindAll(q, cs){
  window.__hitNodes=[]; window.__hitIdx=-1; window.__hitLen=(q||'').length;
  if(!q) return 0;
  var walker=document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);
  var nodes=[]; while(walker.nextNode()){ nodes.push(walker.currentNode); }
  var needle = cs ? q : q.toLowerCase();
  for(var i=0;i<nodes.length;i++){
    var t=nodes[i].nodeValue; if(!t) continue;
    var h = cs ? t : t.toLowerCase();
    var p=0,k;
    while((k=h.indexOf(needle,p))>=0){ window.__hitNodes.push([nodes[i],k]); p=k+needle.length; }
  }
  return window.__hitNodes.length;
}
function docGoHit(i){
  var n=window.__hitNodes.length; if(n===0) return;
  i=((i%n)+n)%n; window.__hitIdx=i;
  var h=window.__hitNodes[i];
  try{
    var r=document.createRange(); r.setStart(h[0],h[1]); r.setEnd(h[0],h[1]+window.__hitLen);
    var sel=window.getSelection(); if(sel){ sel.removeAllRanges(); sel.addRange(r); }
    var rect=r.getBoundingClientRect();
    var top=(rect.top||0)+((window.pageYOffset!==undefined)?window.pageYOffset:(document.documentElement.scrollTop||0));
    window.scrollTo(0, Math.max(0, top-90));
  }catch(e){}
}
</script>";

    private const string PreviewCss = @"
html{background:#1E1E1E}
body{background:#1E1E1E;color:#D4D4D4;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;font-size:14px;line-height:1.65;padding:16px 20px;word-wrap:break-word;margin:0;min-height:100vh}
h1,h2,h3,h4,h5,h6{color:#569CD6;font-weight:600;margin:18px 0 10px}
h1{font-size:1.6em;border-bottom:1px solid #303031;padding-bottom:6px}
h2{font-size:1.35em}
h3{font-size:1.18em}
a{color:#569CD6;text-decoration:none}
strong{color:#569CD6}
em{color:#C586C0}
code{font-family:Consolas,'Courier New',monospace;color:#CE9178;background:#2D2D2D;padding:1px 5px;border-radius:3px}
pre{background:#252526;border:1px solid #303031;border-radius:6px;padding:12px;overflow:auto}
pre code{color:#D4D4D4;background:transparent;padding:0}
blockquote{border-left:3px solid #6A9955;color:#9CA3AF;margin:10px 0;padding:2px 12px}
ul,ol{padding-left:24px}
li{margin:4px 0}
table{border-collapse:collapse;margin:10px 0}
th,td{border:1px solid #3F3F46;padding:6px 10px}
th{background:#2D2D2D}
hr{border:0;border-top:1px solid #303031;margin:16px 0}
img{max-width:100%}
";

    private const string DocCss = @"
html{background:#1E1E1E}
body{background:#1E1E1E;color:#D4D4D4;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;font-size:13px;line-height:1.7;padding:12px 16px;margin:0;word-wrap:break-word}
h1{font-size:1.45em;color:#569CD6;border-bottom:1px solid #303031;padding-bottom:6px;margin:6px 0 12px}
h2{font-size:1.2em;color:#4EC9B0;margin:18px 0 8px;border-bottom:1px solid #2A2A2B;padding-bottom:4px}
h3{font-size:1.06em;color:#C586C0;margin:14px 0 6px}
p,li{margin:5px 0}
a{color:#569CD6;text-decoration:none}
a:hover{text-decoration:underline}
code{font-family:Consolas,'Courier New',monospace;color:#CE9178;background:#2D2D2D;padding:1px 5px;border-radius:3px}
pre{background:#252526;border:1px solid #303031;border-radius:6px;padding:10px;overflow:auto}
pre code{color:#D4D4D4;background:transparent;padding:0}
blockquote{border-left:3px solid #6A9955;color:#9CA3AF;margin:10px 0;padding:2px 12px}
ul,ol{padding-left:22px}
table{border-collapse:collapse;margin:10px 0;font-size:0.95em}
th,td{border:1px solid #3F3F46;padding:5px 9px}
th{background:#2D2D2D}
hr{border:0;border-top:1px solid #303031;margin:14px 0}
strong{color:#DCDCAA}
";

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!_dirty)
        {
            _dirty = true;
            Title = $"在线编辑 - {_file.DisplayName} *";
        }
        UpdateMeta();
        UpdateOverflowHint();
        if (!_suppressHighlightRefresh)
        {
            UpdateOccurrenceHighlights();
            if (FindPanel.Visibility == Visibility.Visible)
                UpdateFindMatches(select: false);
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void UpdateCursor()
    {
        var caret = Editor.TextArea.Caret;
        TxtCursor.Text = $"行 {caret.Line}，列 {caret.Column}";
    }

    private void UpdateMeta()
    {
        TxtMeta.Text = $"{_languageDisplay} · {_encoding.WebName.ToUpperInvariant()} · {(_dirty ? "已修改" : "未修改")}";
    }
}

/// <summary>把一组文本片段标成半透明底色（用于“相同代码”与查找命中高亮）。</summary>
internal sealed class SegmentHighlightRenderer : IBackgroundRenderer
{
    private readonly Brush _fill;

    public SegmentHighlightRenderer(Brush fill) => _fill = fill;

    public List<ISegment> Segments { get; } = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Segments.Count == 0 || !textView.VisualLinesValid) return;
        try
        {
            var builder = new BackgroundGeometryBuilder
            {
                AlignToWholePixels = true,
                CornerRadius = 2
            };
            foreach (var segment in Segments)
                builder.AddSegment(textView, segment);

            var geometry = builder.CreateGeometry();
            if (geometry != null)
                drawingContext.DrawGeometry(_fill, null, geometry);
        }
        catch
        {
            // 渲染异常不影响编辑
        }
    }
}

/// <summary>
/// 右侧简化缩略条（minimap 精简版）：按每条代码行的长度画一根色条，
/// 并用半透明框标出当前可视区域；点击/拖动可定位。
/// </summary>
public sealed class OverviewBar : FrameworkElement
{
    private static readonly Brush BarBrush = Frozen(Color.FromArgb(0xFF, 0x5C, 0x63, 0x70));
    private static readonly Brush HighlightBrush = Frozen(Color.FromArgb(0xFF, 0x4B, 0x8B, 0xF0));
    private static readonly Brush BackBrush = Frozen(Color.FromArgb(0xFF, 0x15, 0x15, 0x15));
    private static readonly Brush ViewportBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Pen ViewportPen = FrozenPen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c)
    {
        var p = new Pen(Frozen(c), 1);
        p.Freeze();
        return p;
    }

    /// <summary>超过该行数就不再逐行画条，避免大文件卡顿。</summary>
    private const int MaxLines = 20000;
    private const double MaxBarHeight = 3;

    private TextEditor? _editor;
    private Geometry? _bars;
    private int _barsLineCount = -1;
    private double _barsWidth = -1;
    private HashSet<int> _highlightLines = new();

    /// <summary>标记“相同代码”所在的行号（供右侧缩略条高亮显示）。</summary>
    public void SetHighlightLines(HashSet<int> lines)
    {
        _highlightLines = lines ?? new HashSet<int>();
        InvalidateVisual();
    }

    public void Attach(TextEditor editor)
    {
        _editor = editor;
        editor.TextChanged += (_, _) => { InvalidateBars(); InvalidateVisual(); };
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
        editor.TextArea.TextView.VisualLinesChanged += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) => { InvalidateBars(); InvalidateVisual(); };
    }

    private void InvalidateBars()
    {
        _bars = null;
        _barsLineCount = -1;
        _barsWidth = -1;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(BackBrush, null, new Rect(0, 0, w, h));

        var editor = _editor;
        var doc = editor?.Document;
        if (editor == null || doc == null || w <= 1 || h <= 1) return;

        EnsureBars(doc, w, h);
        if (_bars != null) dc.DrawGeometry(BarBrush, null, _bars);

        // “相同代码”位置高亮
        if (_highlightLines.Count > 0)
        {
            double rowH = h / doc.LineCount;
            double mh = Math.Max(2, rowH);
            foreach (int ln in _highlightLines)
            {
                if (ln < 1 || ln > doc.LineCount) continue;
                dc.DrawRectangle(HighlightBrush, null, new Rect(0, (ln - 1) * rowH, w, mh));
            }
        }

        // 当前可视区域指示框
        var textView = editor.TextArea.TextView;
        if (textView.VisualLinesValid && doc.LineCount > 1)
        {
            var lines = textView.VisualLines;
            if (lines.Count > 0)
            {
                int first = lines[0].FirstDocumentLine.LineNumber;
                int last = lines[lines.Count - 1].LastDocumentLine.LineNumber;
                double top = (first - 1.0) / doc.LineCount * h;
                double bottom = (double)last / doc.LineCount * h;
                double vh = Math.Max(6, bottom - top);
                if (top + vh > h) top = Math.Max(0, h - vh);
                dc.DrawRectangle(ViewportBrush, ViewportPen, new Rect(0.5, top, Math.Max(0, w - 1), vh));
            }
        }
    }

    private void EnsureBars(TextDocument doc, double w, double h)
    {
        if (_barsWidth > 0 && _barsLineCount == doc.LineCount && Math.Abs(_barsWidth - w) < 0.5) return;
        _barsLineCount = doc.LineCount;
        _barsWidth = w;

        int lineCount = doc.LineCount;
        if (lineCount == 0 || lineCount > MaxLines)
        {
            _bars = null;
            return;
        }

        var lengths = new int[lineCount];
        int maxLen = 1;
        int i = 0;
        foreach (var line in doc.Lines)
        {
            int len = line.Length;
            lengths[i] = len;
            if (len > maxLen) maxLen = len;
            if (++i >= lineCount) break;
        }

        double rowH = h / lineCount;
        double barH = Math.Max(1.0, Math.Min(rowH, MaxBarHeight));
        double yOffset = Math.Max(0, (rowH - barH) / 2);
        double usable = Math.Max(1, w - 6);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int n = 0; n < lineCount; n++)
            {
                if (lengths[n] == 0) continue;
                double bw = Math.Max(2.0, (double)lengths[n] / maxLen * usable);
                double y = n * rowH + yOffset;
                ctx.BeginFigure(new Point(3, y), true, true);
                ctx.LineTo(new Point(3 + bw, y), true, false);
                ctx.LineTo(new Point(3 + bw, y + barH), true, false);
                ctx.LineTo(new Point(3, y + barH), true, false);
            }
        }
        geo.Freeze();
        _bars = geo;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        ScrollToPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) ScrollToPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void ScrollToPoint(Point p)
    {
        var editor = _editor;
        var doc = editor?.Document;
        if (editor == null || doc == null || ActualHeight <= 0) return;
        int line = (int)(p.Y / ActualHeight * doc.LineCount) + 1;
        line = Math.Max(1, Math.Min(doc.LineCount, line));
        editor.ScrollToLine(line);
    }
}
