using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProjectRecorder.Services;

/// <summary>
/// 步骤配图入库前压缩：手机原图动辄几MB/四千像素，直接存会导致
/// 加密文件膨胀、每次保存/读取/渲染全量编解码，界面越用越卡。
/// 规则：最长边超过 1600 才缩小（JPEG q85，PNG 保持 PNG 保透明），小图原样存。
/// </summary>
public static class ImageHelper
{
    private const int MaxSide = 1600;

    public static byte[] PrepareForStorage(byte[] raw)
    {
        if (raw == null || raw.Length == 0) return raw ?? Array.Empty<byte>();
        try
        {
            BitmapImage bmp;
            using (var ms = new MemoryStream(raw))
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }

            if (bmp.PixelWidth <= MaxSide && bmp.PixelHeight <= MaxSide) return raw;

            double scale = Math.Min((double)MaxSide / bmp.PixelWidth, (double)MaxSide / bmp.PixelHeight);
            var small = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
            small.Freeze();

            BitmapEncoder enc;
            if (IsPng(raw))
                enc = new PngBitmapEncoder();
            else
                enc = new JpegBitmapEncoder { QualityLevel = 85 };
            enc.Frames.Add(BitmapFrame.Create(small));
            using var outMs = new MemoryStream();
            enc.Save(outMs);
            byte[] result = outMs.ToArray();
            return result.Length > 0 && result.Length < raw.Length ? result : raw;
        }
        catch
        {
            return raw;
        }
    }

    private static bool IsPng(byte[] raw)
    {
        return raw.Length > 8
            && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47
            && raw[4] == 0x0D && raw[5] == 0x0A && raw[6] == 0x1A && raw[7] == 0x0A;
    }
}
