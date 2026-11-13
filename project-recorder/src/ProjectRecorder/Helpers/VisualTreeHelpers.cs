using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProjectRecorder.Helpers;

/// <summary>
/// 视觉树辅助：取 ListBoxItem 容器、安全取父级。
/// MainWindow 各 Tab 与独立 UserControl 共用，避免每个视图重复实现。
/// </summary>
public static class VisualTreeHelpers
{
    /// <summary>从任意子元素向上找所属 ListBoxItem（限制深度，防止异常树）。</summary>
    public static ListBoxItem? FindListBoxItem(DependencyObject? source)
    {
        DependencyObject? d = source;
        int guard = 0;
        while (d != null && d is not ListBoxItem && guard++ < 50)
            d = SafeParent(d);
        return d as ListBoxItem;
    }

    /// <summary>从事件源向上找所属 ListBoxItem（按钮/图片等点击时定位卡片）。</summary>
    public static ListBoxItem? FindListBoxItemContainer(object sender)
    {
        DependencyObject? d = sender as DependencyObject;
        int guard = 0;
        while (d != null && d is not ListBoxItem && guard++ < 30)
            d = SafeParent(d);
        return d as ListBoxItem;
    }

    /// <summary>防爆的取父级：Run 等非 Visual 文本元素 VisualTreeHelper 会抛异常，改走逻辑树。</summary>
    public static DependencyObject? SafeParent(DependencyObject? node)
    {
        if (node == null) return null;
        try
        {
            return VisualTreeHelper.GetParent(node);
        }
        catch
        {
            try { return LogicalTreeHelper.GetParent(node); }
            catch { return null; }
        }
    }
}
