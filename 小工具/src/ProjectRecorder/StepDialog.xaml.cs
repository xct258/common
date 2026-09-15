using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 操作步骤弹窗：步骤内容（必填，单行）+ 可选插入一张配图。新增与编辑共用。
/// 配图以二进制随数据文件一起 AES 加密存储，不在目录下散落图片文件。
/// </summary>
public partial class StepDialog : Window
{
    private const long MaxImageBytes = 5L * 1024 * 1024;

    public string StepText { get; private set; } = string.Empty;
    public byte[]? ImageData { get; private set; }
    public string ImageName { get; private set; } = string.Empty;

    private readonly InactivityMonitor? _monitor;

    public StepDialog(InactivityMonitor? monitor = null)
    {
        InitializeComponent();
        _monitor = monitor;
        HookActivity();
        TxtStep.Focus();
    }

    public StepDialog(FlowStep existing, InactivityMonitor? monitor = null) : this(monitor)
    {
        Title = "编辑步骤";
        TxtStep.Text = existing.Text;
        ImageData = existing.ImageData;
        ImageName = existing.ImageName ?? string.Empty;
        RefreshImageUi();
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

    private void BtnPickImage_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        var dlg = new OpenFileDialog
        {
            Title = "选择步骤配图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取图片失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (bytes.Length > MaxImageBytes)
        {
            MessageBox.Show("图片超过 5MB，请压缩后再插入。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ImageData = bytes;
        ImageName = Path.GetFileName(dlg.FileName);
        RefreshImageUi();
    }

    private void BtnClearImage_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        ImageData = null;
        ImageName = string.Empty;
        RefreshImageUi();
    }

    private void RefreshImageUi()
    {
        bool has = ImageData != null && ImageData.Length > 0;
        PanelImagePreview.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has)
        {
            ImgPreview.Source = null;
            return;
        }

        TxtImageInfo.Text = $"已插入：{ImageName}（{ImageData!.Length / 1024} KB）";
        try
        {
            using var ms = new MemoryStream(ImageData);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgPreview.Source = bmp;
        }
        catch
        {
            TxtImageInfo.Text = "图片无法预览，确定后仍会保存原文件";
            ImgPreview.Source = null;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtStep.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show("请填写步骤内容。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtStep.Focus();
            return;
        }

        StepText = text;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
