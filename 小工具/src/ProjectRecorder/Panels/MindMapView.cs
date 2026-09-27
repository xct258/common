using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Panels;

/// <summary>
/// 思维导图画布：树形自动布局（根在左、子级向右展开）。
/// 双击节点弹窗编辑、右键增删节点、空白处拖动平移、滚轮缩放。
/// 数据变化后触发 Changed，由宿主负责加密落盘。
/// </summary>
public class MindMapView : UserControl
{
    private const double HGap = 56;
    private const double VGap = 14;
    private const double PadX = 14;
    private const double PadY = 8;
    private const double MinNodeWidth = 56;
    private const double MaxTextWidth = 200;
    private const double EdgeGap = 36;
    private const double MinScale = 0.35;
    private const double MaxScale = 2.5;

    private static readonly FontFamily NodeFont = new("Microsoft YaHei UI, Segoe UI");
    private static readonly Brush RootBg = Frozen(0x25, 0x63, 0xEB);
    private static readonly Brush RootFg = Brushes.White;
    private static readonly Brush RootBorder = Frozen(0x1D, 0x4E, 0xD8);
    private static readonly Brush L1Bg = Frozen(0xDB, 0xEA, 0xFE);
    private static readonly Brush L1Fg = Frozen(0x1E, 0x40, 0xAF);
    private static readonly Brush L1Border = Frozen(0x93, 0xC5, 0xFD);
    private static readonly Brush L2Bg = Frozen(0xEF, 0xF6, 0xFF);
    private static readonly Brush L2Fg = Frozen(0x1F, 0x29, 0x37);
    private static readonly Brush L2Border = Frozen(0xBF, 0xDB, 0xFE);
    private static readonly Brush DeepBg = Brushes.White;
    private static readonly Brush DeepFg = Frozen(0x37, 0x41, 0x51);
    private static readonly Brush DeepBorder = Frozen(0xE2, 0xE8, 0xF0);
    private static readonly Brush EdgeBrush = Frozen(0x93, 0xC5, 0xFD);
    private static readonly Brush SelectedBorder = Frozen(0xF5, 0x9E, 0x0B);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed class NodeVisual
    {
        public MindNode Node = null!;
        public int Depth;
        public double X, Y, W, H, SubH;
        public List<NodeVisual> Children = new();
        public Border? Box;
    }

    private readonly ScrollViewer _scroll;
    private readonly Canvas _surface;
    private MindMap? _map;
    private NodeVisual? _root;
    private string? _selectedId;
    private double _scale = 1.0;
    private double _contentW;
    private double _contentH;

    private bool _panning;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;

    /// <summary>无操作计时器（编辑弹窗内也计时），由宿主注入。</summary>
    public InactivityMonitor? Monitor { get; set; }

    /// <summary>节点标题/内容或结构变化后触发（宿主据此保存）。</summary>
    public event Action? Changed;
    /// <summary>缩放比例变化（1.0 = 100%）。</summary>
    public event Action<double>? ZoomChanged;

    public MindMapView()
    {
        _surface = new Canvas
        {
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Focusable = true,
            FocusVisualStyle = null
        };
        _scroll = new ScrollViewer
        {
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
            Focusable = false
        };
        Content = _scroll;

        _scroll.PreviewMouseWheel += OnWheel;
        _surface.PreviewKeyDown += OnSurfaceKeyDown;
        _surface.MouseLeftButtonDown += OnSurfaceMouseDown;
        _surface.MouseMove += OnSurfaceMouseMove;
        _surface.MouseLeftButtonUp += OnSurfaceMouseUp;
        _surface.MouseRightButtonDown += OnSurfaceRightButtonDown;
    }

    public MindMap? Map => _map;

    // ---------- 对外操作 ----------

    public void SetMap(MindMap? map)
    {
        _map = map;
        _selectedId = map?.Root?.Id;
        Rebuild();
        Dispatcher.BeginInvoke(new Action(Fit), DispatcherPriority.Loaded);
    }

    /// <summary>新建导图后直接打开中心主题编辑弹窗。</summary>
    public void BeginEditRoot()
    {
        if (_root != null) EditNode(_root);
    }

    public void ZoomIn() => SetScaleKeepCenter(_scale * 1.15);

    public void ZoomOut() => SetScaleKeepCenter(_scale / 1.15);

    public void Fit()
    {
        if (_contentW <= 0 || _contentH <= 0 || _scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0)
        {
            SetScale(1.0);
            return;
        }
        double s = Math.Min(_scroll.ViewportWidth / _contentW, _scroll.ViewportHeight / _contentH);
        s = Math.Max(MinScale, Math.Min(1.0, s));
        SetScale(s);
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    /// <summary>
    /// 导出整张导图为 PNG（白底、2 倍分辨率、不含当前缩放与选中高亮）。
    /// </summary>
    public void ExportPng(string path)
    {
        if (_root == null || _contentW <= 0 || _contentH <= 0)
            throw new InvalidOperationException("导图为空，无法导出。");

        double w = _contentW;
        double h = _contentH;
        double scale = w > 15000 || h > 15000 ? 1.0 : 2.0;

        var oldTransform = _surface.LayoutTransform;
        var oldBackground = _surface.Background;
        string? keepSelection = _selectedId;
        try
        {
            // 按 100% 未缩放渲染，白色底，去掉选中高亮
            _selectedId = null;
            ApplyBoxState(_root);
            _surface.LayoutTransform = Transform.Identity;
            _surface.Background = Brushes.White;
            _surface.UpdateLayout();

            var bmp = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(w * scale)),
                Math.Max(1, (int)Math.Ceiling(h * scale)),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(_surface);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            encoder.Save(fs);
        }
        finally
        {
            _selectedId = keepSelection;
            ApplyBoxState(_root);
            _surface.Background = oldBackground;
            _surface.LayoutTransform = oldTransform;
            _surface.UpdateLayout();
        }
    }

    // ---------- 布局与渲染 ----------

    private void Rebuild()
    {
        _surface.Children.Clear();
        _root = null;
        _contentW = 0;
        _contentH = 0;

        if (_map?.Root == null)
        {
            _surface.Width = 0;
            _surface.Height = 0;
            return;
        }

        _root = BuildVisual(_map.Root, 0);
        MeasureVisual(_root);
        Place(_root, EdgeGap, EdgeGap);

        _contentW = MaxRight(_root) + EdgeGap;
        _contentH = _root.SubH + EdgeGap * 2;
        _surface.Width = _contentW;
        _surface.Height = _contentH;
        _surface.LayoutTransform = new ScaleTransform(_scale, _scale);

        // 先画连线，再画节点（节点盖在连线上）
        AddEdges(_root);
        AddNodeBoxes(_root);
    }

    private static NodeVisual BuildVisual(MindNode node, int depth)
    {
        var v = new NodeVisual { Node = node, Depth = depth };
        foreach (var child in node.Children ?? new List<MindNode>())
            v.Children.Add(BuildVisual(child, depth + 1));
        return v;
    }

    private void MeasureVisual(NodeVisual v)
    {
        var (w, h) = MeasureNode(v.Node, v.Depth);
        v.W = w;
        v.H = h;
        foreach (var c in v.Children) MeasureVisual(c);

        if (v.Children.Count == 0)
        {
            v.SubH = v.H;
            return;
        }
        double sum = VGap * (v.Children.Count - 1);
        foreach (var c in v.Children) sum += c.SubH;
        v.SubH = Math.Max(v.H, sum);
    }

    private (double W, double H) MeasureNode(MindNode node, int depth)
    {
        double fontSize = depth == 0 ? 15 : 14;
        var weight = depth == 0 ? FontWeights.Bold
            : depth == 1 ? FontWeights.SemiBold
            : FontWeights.Normal;
        var typeface = new Typeface(NodeFont, FontStyles.Normal, weight, FontStretches.Normal);

        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { /* 未接入视觉树时用默认 */ }

        var ft = new FormattedText(
            node.Title ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            dpi)
        {
            MaxTextWidth = MaxTextWidth,
            MaxLineCount = 6,
            Trimming = TextTrimming.CharacterEllipsis
        };

        double textW = Math.Min(ft.Width, MaxTextWidth);
        double w = Math.Min(Math.Max(textW, MinNodeWidth - PadX * 2), MaxTextWidth) + PadX * 2;
        double h = ft.Height + PadY * 2;
        return (w, h);
    }

    private static void Place(NodeVisual v, double x, double top)
    {
        v.X = x;
        v.Y = top + (v.SubH - v.H) / 2;

        double childX = x + v.W + HGap;
        double childTop = top;
        foreach (var c in v.Children)
        {
            Place(c, childX, childTop);
            childTop += c.SubH + VGap;
        }
    }

    private static double MaxRight(NodeVisual v)
    {
        double max = v.X + v.W;
        foreach (var c in v.Children) max = Math.Max(max, MaxRight(c));
        return max;
    }

    private void AddEdges(NodeVisual v)
    {
        foreach (var c in v.Children)
        {
            double x1 = v.X + v.W, y1 = v.Y + v.H / 2;
            double x2 = c.X, y2 = c.Y + c.H / 2;
            double mx = (x1 + x2) / 2;

            var figure = new PathFigure { StartPoint = new Point(x1, y1) };
            figure.Segments.Add(new BezierSegment(
                new Point(mx, y1), new Point(mx, y2), new Point(x2, y2), true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            _surface.Children.Add(new Path
            {
                Data = geometry,
                Stroke = EdgeBrush,
                StrokeThickness = v.Depth == 0 ? 2.4 : 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            AddEdges(c);
        }
    }

    private void AddNodeBoxes(NodeVisual v)
    {
        v.Box = MakeNodeBox(v);
        Canvas.SetLeft(v.Box, v.X);
        Canvas.SetTop(v.Box, v.Y);
        _surface.Children.Add(v.Box);
        ApplyBoxState(v);

        foreach (var c in v.Children) AddNodeBoxes(c);
    }

    private Border MakeNodeBox(NodeVisual v)
    {
        var (bg, fg, _) = NodeColors(v.Depth);
        var text = new TextBlock
        {
            Text = v.Node.Title ?? string.Empty,
            FontFamily = NodeFont,
            FontSize = v.Depth == 0 ? 15 : 14,
            FontWeight = v.Depth == 0 ? FontWeights.Bold
                : v.Depth == 1 ? FontWeights.SemiBold
                : FontWeights.Normal,
            Foreground = fg,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var box = new Border
        {
            Width = v.W,
            Height = v.H,
            CornerRadius = new CornerRadius(8),
            Background = bg,
            BorderThickness = new Thickness(1),
            Child = text,
            Cursor = Cursors.Hand,
            Tag = v,
            ToolTip = NodeToolTip(v.Node)
        };
        box.MouseLeftButtonDown += Node_MouseLeftButtonDown;
        box.MouseRightButtonDown += Node_MouseRightButtonDown;
        box.ContextMenu = BuildNodeMenu(v);
        return box;
    }

    private static string NodeToolTip(MindNode node)
    {
        string content = (node.Content ?? string.Empty).Trim();
        if (content.Length == 0) return "双击弹窗编辑（标题 + 内容）· 右键增删节点";
        return content.Length > 300 ? content.Substring(0, 300) + "…" : content;
    }

    private static (Brush Bg, Brush Fg, Brush Border) NodeColors(int depth)
    {
        switch (depth)
        {
            case 0: return (RootBg, RootFg, RootBorder);
            case 1: return (L1Bg, L1Fg, L1Border);
            case 2: return (L2Bg, L2Fg, L2Border);
            default: return (DeepBg, DeepFg, DeepBorder);
        }
    }

    private void ApplyBoxState(NodeVisual v)
    {
        if (v.Box == null) return;
        var (_, _, border) = NodeColors(v.Depth);
        bool selected = v.Node.Id == _selectedId;
        v.Box.BorderBrush = selected ? SelectedBorder : border;
        v.Box.BorderThickness = new Thickness(selected ? 2 : 1);
        foreach (var c in v.Children) ApplyBoxState(c);
    }

    // ---------- 选择 ----------

    private void SelectNode(NodeVisual? v)
    {
        _selectedId = v?.Node.Id;
        if (_root != null) ApplyBoxState(_root);
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not NodeVisual v) return;
        var current = FindCurrent(v);
        if (current == null) return;

        SelectNode(current);
        _surface.Focus();
        if (e.ClickCount == 2)
        {
            EditNode(current);
            e.Handled = true;
        }
    }

    private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not NodeVisual v) return;
        var current = FindCurrent(v);
        if (current != null)
        {
            SelectNode(current);
            _surface.Focus();
        }
    }

    private NodeVisual? FindCurrent(NodeVisual stale)
    {
        return _root == null ? null : FindVisual(_root, stale.Node.Id);
    }

    // ---------- 节点增删 ----------

    private ContextMenu BuildNodeMenu(NodeVisual v)
    {
        var menu = new ContextMenu();

        var addChild = new MenuItem { Header = "添加子节点" };
        addChild.Click += (_, _) => AddChild(v);
        menu.Items.Add(addChild);

        if (v.Depth > 0)
        {
            var addSibling = new MenuItem { Header = "添加同级节点" };
            addSibling.Click += (_, _) => AddSibling(v);
            menu.Items.Add(addSibling);
        }

        var edit = new MenuItem { Header = "编辑标题/内容" };
        edit.Click += (_, _) => EditNodeById(v.Node.Id);
        menu.Items.Add(edit);

        if (v.Depth > 0)
        {
            menu.Items.Add(new Separator());
            var del = new MenuItem { Header = "删除节点" };
            del.Click += (_, _) => DeleteNode(v);
            menu.Items.Add(del);
        }
        return menu;
    }

    private void AddChild(NodeVisual v)
    {
        var current = FindCurrent(v);
        if (current == null) return;

        var child = new MindNode { Title = "新节点" };
        current.Node.Children.Add(child);
        _selectedId = child.Id;
        Changed?.Invoke();
        Rebuild();
        EditNodeById(child.Id);
    }

    private void AddSibling(NodeVisual v)
    {
        var current = FindCurrent(v);
        var parent = current == null || _root == null ? null : FindParent(_root, current);
        if (current == null || parent == null) return;

        var sibling = new MindNode { Title = "新节点" };
        int index = parent.Node.Children.IndexOf(current.Node);
        parent.Node.Children.Insert(index < 0 ? parent.Node.Children.Count : index + 1, sibling);
        _selectedId = sibling.Id;
        Changed?.Invoke();
        Rebuild();
        EditNodeById(sibling.Id);
    }

    private void DeleteNode(NodeVisual v)
    {
        var current = FindCurrent(v);
        var parent = current == null || _root == null ? null : FindParent(_root, current);
        if (current == null || parent == null) return;

        int count = CountSubtree(current.Node);
        string name = string.IsNullOrWhiteSpace(current.Node.Title) ? "未命名" : current.Node.Title.Trim();
        string msg = count > 1
            ? $"确定删除「{name}」及其 {count - 1} 个子节点吗？"
            : $"确定删除「{name}」吗？";
        if (MessageBox.Show(msg, "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            _surface.Focus();
            return;
        }

        parent.Node.Children.Remove(current.Node);
        if (_selectedId == current.Node.Id) _selectedId = parent.Node.Id;
        Changed?.Invoke();
        Rebuild();
        _surface.Focus();
    }

    private static int CountSubtree(MindNode node)
    {
        int count = 1;
        foreach (var c in node.Children ?? new List<MindNode>()) count += CountSubtree(c);
        return count;
    }

    private static NodeVisual? FindVisual(NodeVisual root, string id)
    {
        if (root.Node.Id == id) return root;
        foreach (var c in root.Children)
        {
            var hit = FindVisual(c, id);
            if (hit != null) return hit;
        }
        return null;
    }

    private static NodeVisual? FindParent(NodeVisual root, NodeVisual target)
    {
        foreach (var c in root.Children)
        {
            if (ReferenceEquals(c, target)) return root;
            var hit = FindParent(c, target);
            if (hit != null) return hit;
        }
        return null;
    }

    // ---------- 节点编辑（标题 + 内容弹窗） ----------

    private void EditNodeById(string nodeId)
    {
        if (_root == null) return;
        var v = FindVisual(_root, nodeId);
        if (v != null) EditNode(v);
    }

    private void EditNode(NodeVisual v)
    {
        var current = FindCurrent(v);
        if (current == null) return;

        var dlg = new MindNodeDialog(current.Node.Title, current.Node.Content, Monitor)
        {
            Owner = Window.GetWindow(this)
        };
        bool ok = dlg.ShowDialog() == true;
        _surface.Focus();
        if (!ok) return;

        if (dlg.NodeTitle == current.Node.Title && dlg.NodeContent == current.Node.Content) return;
        current.Node.Title = dlg.NodeTitle;
        current.Node.Content = dlg.NodeContent;
        Changed?.Invoke();
        Rebuild();
    }

    // ---------- 平移 / 缩放 ----------

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _surface)) return;

        SelectNode(null);
        _panning = true;
        _panStart = e.GetPosition(_scroll);
        _panOffsetX = _scroll.HorizontalOffset;
        _panOffsetY = _scroll.VerticalOffset;
        _surface.CaptureMouse();
        _surface.Focus();
        _surface.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(_scroll);
        _scroll.ScrollToHorizontalOffset(_panOffsetX - (p.X - _panStart.X));
        _scroll.ScrollToVerticalOffset(_panOffsetY - (p.Y - _panStart.Y));
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        _surface.ReleaseMouseCapture();
        _surface.Cursor = Cursors.Arrow;
    }

    // 键盘操作：Tab 子节点 / Enter 同级 / F2 弹窗编辑 / Delete 删除 / 方向键移动选择 / Esc 取消选择
    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (_root == null || _selectedId == null)
        {
            if (e.Key == Key.Escape) e.Handled = true;
            return;
        }
        var v = FindVisual(_root, _selectedId);
        if (v == null) return;

        switch (e.Key)
        {
            case Key.Tab:
                AddChild(v);
                e.Handled = true;
                break;
            case Key.Enter:
                if (v.Depth > 0) AddSibling(v);
                else EditNode(v);
                e.Handled = true;
                break;
            case Key.F2:
                EditNode(v);
                e.Handled = true;
                break;
            case Key.Delete:
                if (v.Depth > 0) DeleteNode(v);
                e.Handled = true;
                break;
            case Key.Left:
                {
                    var parent = FindParent(_root, v);
                    if (parent != null) SelectNode(parent);
                    e.Handled = true;
                    break;
                }
            case Key.Right:
                if (v.Children.Count > 0) SelectNode(v.Children[0]);
                e.Handled = true;
                break;
            case Key.Up:
            case Key.Down:
                {
                    var parent = v.Depth == 0 ? null : FindParent(_root, v);
                    if (parent != null)
                    {
                        int i = parent.Children.FindIndex(c => ReferenceEquals(c, v));
                        int j = e.Key == Key.Up ? i - 1 : i + 1;
                        if (j >= 0 && j < parent.Children.Count) SelectNode(parent.Children[j]);
                    }
                    e.Handled = true;
                    break;
                }
            case Key.Escape:
                SelectNode(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSurfaceRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _surface)) return;
        if (_root == null) return;

        var menu = new ContextMenu();
        var addChild = new MenuItem { Header = "给中心主题添加子节点" };
        addChild.Click += (_, _) => AddChild(_root);
        menu.Items.Add(addChild);
        var edit = new MenuItem { Header = "编辑中心主题" };
        edit.Click += (_, _) => EditNode(_root);
        menu.Items.Add(edit);

        menu.PlacementTarget = _surface;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        // 滚轮直接缩放（以鼠标位置为中心），画布平移用空白处拖动或滚动条
        e.Handled = true;
        double factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        double target = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
        if (Math.Abs(target - _scale) < 0.0001) return;

        double old = _scale;
        var p = e.GetPosition(_scroll);
        _scale = target;
        _surface.LayoutTransform = new ScaleTransform(_scale, _scale);
        _surface.UpdateLayout();

        double localX = (p.X + _scroll.HorizontalOffset) / old;
        double localY = (p.Y + _scroll.VerticalOffset) / old;
        _scroll.ScrollToHorizontalOffset(localX * _scale - p.X);
        _scroll.ScrollToVerticalOffset(localY * _scale - p.Y);
        ZoomChanged?.Invoke(_scale);
    }

    private void SetScaleKeepCenter(double scale)
    {
        scale = Math.Max(MinScale, Math.Min(MaxScale, scale));
        if (Math.Abs(scale - _scale) < 0.0001) return;

        double old = _scale;
        _scale = scale;
        _surface.LayoutTransform = new ScaleTransform(_scale, _scale);
        _surface.UpdateLayout();

        double offsetX = (_scroll.HorizontalOffset + _scroll.ViewportWidth / 2) / old * _scale - _scroll.ViewportWidth / 2;
        double offsetY = (_scroll.VerticalOffset + _scroll.ViewportHeight / 2) / old * _scale - _scroll.ViewportHeight / 2;
        _scroll.ScrollToHorizontalOffset(Math.Max(0, offsetX));
        _scroll.ScrollToVerticalOffset(Math.Max(0, offsetY));
        ZoomChanged?.Invoke(_scale);
    }

    private void SetScale(double scale)
    {
        scale = Math.Max(MinScale, Math.Min(MaxScale, scale));
        _scale = scale;
        _surface.LayoutTransform = new ScaleTransform(_scale, _scale);
        ZoomChanged?.Invoke(_scale);
    }
}
