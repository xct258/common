using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 图片放大查看：按原图尺寸在可滚动窗口中显示。
/// 窗内的操作同样上报无操作计时。
/// </summary>
public partial class ImageViewerDialog : Window
{
    public ImageViewerDialog(byte[] imageData, string title, InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        Title = string.IsNullOrWhiteSpace(title) ? "查看图片" : $"查看图片 - {title}";

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

        try
        {
            using var ms = new MemoryStream(imageData);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgFull.Source = bmp;
        }
        catch
        {
            MessageBox.Show("图片数据损坏，无法显示。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
