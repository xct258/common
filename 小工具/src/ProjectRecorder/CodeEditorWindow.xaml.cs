using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
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
        Editor.Text = text;
        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursor();
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            AutoSave();
            RefreshPreview();
        };
        // 预览里的外部链接不跳转（避免预览被导航走）
        PreviewBrowser.Navigating += (s, ev) =>
        {
            if (ev.Uri != null && (ev.Uri.Scheme == "http" || ev.Uri.Scheme == "https" || ev.Uri.Scheme == "file"))
                ev.Cancel = true;
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

        ChkWrap.IsChecked = _language == "MarkDown";
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

    private void ChkWrap_Changed(object sender, RoutedEventArgs e)
    {
        if (Editor == null) return;
        Editor.WordWrap = ChkWrap.IsChecked == true;
    }

    // ---------- Markdown 预览（仅 .md，右侧分栏，编辑停顿后自动刷新） ----------

    private void ChkPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (Editor == null || PreviewPane == null || PreviewSplitter == null) return;
        bool on = ChkPreview.IsChecked == true;
        PreviewSplitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PreviewPane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitterCol.Width = on ? new GridLength(4) : new GridLength(0);
        PreviewCol.Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (on)
        {
            _lastPreviewText = null; // 面板刚显示时强制渲染一次（浏览器可能是空白页）
            RefreshPreview();
        }
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

    private static string BuildMarkdownHtml(string markdown)
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
               "<style>" + PreviewCss + "</style></head><body>" + body + "</body></html>";
    }

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

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!_dirty)
        {
            _dirty = true;
            Title = $"在线编辑 - {_file.DisplayName} *";
        }
        UpdateMeta();
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
