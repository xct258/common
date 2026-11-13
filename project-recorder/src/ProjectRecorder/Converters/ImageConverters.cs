using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Converters;

/// <summary>
/// 步骤配图对象 → 列表缩略图（绑定整个 StepImage，按图片 Id 缓存已解码位图）。
/// 图片字节优先取内存（新插入未保存），否则从独立加密文件读（Data/images/&lt;id&gt;.img）；
/// 解码只在每张图第一次显示时发生，列表刷新/步骤增删排序都不会重复解码。
/// </summary>
public class StepImageToThumbConverter : IValueConverter
{
    private const int ThumbWidth = 480;
    private const int MaxCache = 150;

    private readonly Dictionary<string, BitmapImage> _cache = new();
    private readonly Queue<string> _order = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not StepImage img) return null;

        string key = img.Id ?? string.Empty;
        if (key.Length > 0 && _cache.TryGetValue(key, out var cached))
            return cached;

        byte[]? bytes = DataStore.GetImageBytes(img);
        if (bytes == null || bytes.Length == 0) return null;

        BitmapImage bmp;
        try
        {
            bmp = CreateThumb(bytes);
        }
        catch
        {
            return null;
        }

        if (key.Length > 0)
        {
            _cache[key] = bmp;
            _order.Enqueue(key);
            while (_cache.Count > MaxCache && _order.Count > 0)
            {
                string old = _order.Dequeue();
                if (old == key) continue;
                _cache.Remove(old);
            }
        }
        return bmp;
    }

    private static BitmapImage CreateThumb(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = ThumbWidth;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>集合非空则显示，否则折叠。</summary>
public class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Collections.IEnumerable e)
        {
            foreach (var _ in e) return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
