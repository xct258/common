using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 图片放大查看：打开默认适应窗口显示全图，滚轮直接缩放（10%~800%），
/// 放大后按住图片拖动平移；同一步骤多图可上一张/下一张翻看。
/// 窗内的操作同样上报无操作计时。
/// </summary>
public partial class ImageViewerDialog : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8.0;
    private const double WheelStep = 1.25;

    private readonly InactivityMonitor? _monitor;
    private readonly string _baseTitle;
    private readonly List<StepImage> _images = new();
    private int _index;
    private double _zoom = 1.0;
    private double _imgW;
    private double _imgH;

    // 适应窗口模式：为 true 时窗口大小变化会自动重算（手动缩放后关闭）
    private bool _fitMode = true;
    private int _fitRetries;

    // 按住拖动平移
    private bool _dragging;
    private Point _dragStart;
    private double _dragStartH;
    private double _dragStartV;

    public ImageViewerDialog(byte[] imageData, string title, InactivityMonitor? monitor = null)
        : this(new List<StepImage> { new StepImage { Data = imageData } }, 0, title, monitor)
    {
    }

    public ImageViewerDialog(IList<StepImage> images, int index, string title, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        _baseTitle = string.IsNullOrWhiteSpace(title) ? "查看图片" : $"查看图片 - {title}";

        if (images != null)
        {
            foreach (var im in images)
            {
                if (im == null) continue;
                if ((im.Data != null && im.Data.Length > 0) || !string.IsNullOrEmpty(im.Id))
                    _images.Add(im);
            }
        }
        if (_images.Count == 0)
        {
            MessageBox.Show("图片数据损坏，无法显示。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }
        if (index < 0) index = 0;
        if (index >= _images.Count) index = _images.Count - 1;
        _index = index;

        if (monitor != null)
        {
            PreviewMouseMove += (s, e) => monitor.NotifyActivity();
            PreviewMouseDown += (s, e) => monitor.NotifyActivity();
            PreviewMouseUp += (s, e) => monitor.NotifyActivity();
            PreviewMouseWheel += (s, e) => monitor.NotifyActivity();
            PreviewKeyDown += (s, e) => monitor.NotifyActivity();
            PreviewKeyUp += (s, e) => monitor.NotifyActivity();
            PreviewTextInput += (s, e) => monitor.NotifyActivity();
        }

        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(ShowImage), DispatcherPriority.Loaded);
        // 首次渲染完成后兜底再适应一次，避免个别情况下视口尺寸尚未就绪导致图片偏小
        ContentRendered += (_, _) => { if (_fitMode) FitToWindow(); };
    }

    private void ShowImage()
    {
        var img = _images[_index];
        try
        {
            byte[]? data = DataStore.GetImageBytes(img);
            if (data == null || data.Length == 0)
            {
                MessageBox.Show("图片数据损坏，无法显示。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgFull.Source = bmp;
            _imgW = bmp.PixelWidth;
            _imgH = bmp.PixelHeight;
        }
        catch
        {
            MessageBox.Show("图片数据损坏，无法显示。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        // 打开默认适应窗口，完整显示全图（等布局完成后算，视口没好就稍后重试）
        _fitMode = true;
        _fitRetries = 0;
        FitToWindow();
        RefreshNav();
    }

    private void RefreshNav()
    {
        if (_images.Count > 1)
        {
            NavPanel.Visibility = Visibility.Visible;
            TxtCounter.Text = $"{_index + 1}/{_images.Count}";
            BtnPrev.IsEnabled = _index > 0;
            BtnNext.IsEnabled = _index < _images.Count - 1;
            Title = $"{_baseTitle}（{_index + 1}/{_images.Count}）";
        }
        else
        {
            NavPanel.Visibility = Visibility.Collapsed;
            Title = _baseTitle;
        }
    }

    private void ApplyZoom()
    {
        ImgFull.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        TxtZoom.Text = $"{_zoom * 100:0}%";
    }

    // 以视口中心为锚点缩放，避免图片跳动（手动缩放后退出适应模式）
    private void ZoomTo(double zoom)
    {
        if (zoom < MinZoom) zoom = MinZoom;
        if (zoom > MaxZoom) zoom = MaxZoom;
        _fitMode = false;

        double relX = 0.5, relY = 0.5;
        if (Scroller.ExtentWidth > 0 && Scroller.ExtentHeight > 0)
        {
            relX = (Scroller.HorizontalOffset + Scroller.ViewportWidth / 2) / Scroller.ExtentWidth;
            relY = (Scroller.VerticalOffset + Scroller.ViewportHeight / 2) / Scroller.ExtentHeight;
        }

        _zoom = zoom;
        ApplyZoom();
        Scroller.UpdateLayout();

        if (Scroller.ExtentWidth > 0 && Scroller.ExtentHeight > 0)
        {
            Scroller.ScrollToHorizontalOffset(relX * Scroller.ExtentWidth - Scroller.ViewportWidth / 2);
            Scroller.ScrollToVerticalOffset(relY * Scroller.ExtentHeight - Scroller.ViewportHeight / 2);
        }
        _monitor?.NotifyActivity();
    }

    private void FitToWindow()
    {
        if (_imgW <= 0 || _imgH <= 0) return;

        // Viewport 在布局尚未完成时可能为 0，退回用控件实际尺寸
        double vw = Scroller.ViewportWidth;
        double vh = Scroller.ViewportHeight;
        if (vw <= 1 || vh <= 1)
        {
            vw = Scroller.ActualWidth;
            vh = Scroller.ActualHeight;
        }
        double availW = vw - 16;
        double availH = vh - 16;
        if (availW <= 0 || availH <= 0)
        {
            // 布局还没完成，等一帧再算（最多重试几次，避免空转）
            if (_fitRetries < 8)
            {
                _fitRetries++;
                Dispatcher.BeginInvoke(new Action(FitToWindow), DispatcherPriority.Loaded);
            }
            return;
        }
        _fitRetries = 0;
        double z = Math.Min(availW / _imgW, availH / _imgH);
        if (z < MinZoom) z = MinZoom;
        if (z > MaxZoom) z = MaxZoom;
        _zoom = z;
        _fitMode = true;
        ApplyZoom();
        Scroller.ScrollToHome();
        _monitor?.NotifyActivity();
    }

    // 适应模式下窗口变化自动重算；手动缩放后不再跟随。
    // SizeChanged 触发时视口往往还是旧尺寸，延后到布局完成再算，避免放大后仍显示偏小
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode && _imgW > 0 && IsLoaded)
            Dispatcher.BeginInvoke(new Action(FitToWindow), DispatcherPriority.Loaded);
    }

    private void Scroller_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomTo(_zoom * (e.Delta > 0 ? WheelStep : 1.0 / WheelStep));
        e.Handled = true;
    }

    // 放大后按住图片直接拖动平移（只响应图片区域，不拦截滚动条）
    private void Scroller_DragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != ImgFull) return;
        _dragging = true;
        _dragStart = e.GetPosition(Scroller);
        _dragStartH = Scroller.HorizontalOffset;
        _dragStartV = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
        Scroller.Cursor = Cursors.ScrollAll;
    }

    private void Scroller_DragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        Point p = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_dragStartH - (p.X - _dragStart.X));
        Scroller.ScrollToVerticalOffset(_dragStartV - (p.Y - _dragStart.Y));
    }

    private void Scroller_DragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Scroller.ReleaseMouseCapture();
        Scroller.Cursor = Cursors.Hand;
        _monitor?.NotifyActivity();
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ZoomTo(_zoom * WheelStep);
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ZoomTo(_zoom / WheelStep);
    private void BtnActual_Click(object sender, RoutedEventArgs e) => ZoomTo(1.0);
    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitToWindow();

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; ShowImage(); }
        _monitor?.NotifyActivity();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _images.Count - 1) { _index++; ShowImage(); }
        _monitor?.NotifyActivity();
    }
}
