using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 操作步骤弹窗：步骤内容（必填）+ 可插入多张配图（可多选追加，单张点 × 移除）。
/// 内容区可滚动，底部确定/取消常驻可见。新增与编辑共用。
/// 配图以二进制随数据文件一起 AES 加密存储，不在目录下散落图片文件。
/// </summary>
public partial class StepDialog : Window
{
    private const long MaxImageBytes = 5L * 1024 * 1024;

    public string StepText { get; private set; } = string.Empty;
    public List<StepImage> Images { get; } = new();

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
        if (existing.Images != null)
        {
            foreach (var im in existing.Images)
            {
                if (im == null) continue;
                byte[]? bytes = DataStore.GetImageBytes(im);
                if (bytes != null && bytes.Length > 0)
                    Images.Add(new StepImage { Data = bytes, Name = im.Name ?? string.Empty });
            }
        }
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
            Title = "选择步骤配图（可多选）",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;

        int skipped = 0;
        foreach (string file in dlg.FileNames)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取图片失败：{file}\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                continue;
            }

            if (bytes.Length > MaxImageBytes)
            {
                skipped++;
                continue;
            }

            // 入库前压缩，大图存进来整个软件都会变卡
            bytes = ImageHelper.PrepareForStorage(bytes);
            Images.Add(new StepImage { Data = bytes, Name = Path.GetFileName(file) });
        }

        if (skipped > 0)
            MessageBox.Show($"{skipped} 张图片超过 5MB，已跳过（其余已加入）。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);

        RefreshImageUi();
    }

    private void BtnRemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if ((sender as FrameworkElement)?.DataContext is StepImage img)
        {
            Images.RemoveAll(x => x.Id == img.Id);
            RefreshImageUi();
        }
    }

    private void RefreshImageUi()
    {
        bool has = Images.Count > 0;
        PanelImagePreview.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has)
        {
            LstImages.ItemsSource = null;
            return;
        }

        long totalKb = Images.Sum(x => (DataStore.GetImageBytes(x)?.Length ?? 0) / 1024);
        TxtImageInfo.Text = $"已插入 {Images.Count} 张（共 {totalKb} KB），点 × 移除单张，可继续插入追加";
        LstImages.ItemsSource = null;
        LstImages.ItemsSource = new List<StepImage>(Images);
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
