using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectRecorder.Helpers;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// MainWindow 拆分文件：通用视觉树辅助：空白处拦截右键、查找 ListBoxItem 容器、安全取父级
/// 与 MainWindow.xaml.cs 同为一个 partial 类，可直接访问其私有字段和 x:Name 生成的控件字段。
/// </summary>
public partial class MainWindow
{
    // 空白处松开右键也不弹菜单（菜单在松开时触发，只拦按下不够）
    private void ListBlankUp(object sender, MouseButtonEventArgs e)
    {
        if (VisualTreeHelpers.FindListBoxItem(e.OriginalSource as DependencyObject) == null)
            e.Handled = true;
    }

    private static ListBoxItem? FindListBoxItem(DependencyObject? src)
        => VisualTreeHelpers.FindListBoxItem(src);

    // 防爆的取父级：Run 等非 Visual 文本元素 VisualTreeHelper 会抛异常，改走逻辑树
    private static DependencyObject? SafeParent(DependencyObject? d)
        => VisualTreeHelpers.SafeParent(d);

    private static ListBoxItem? FindListBoxItemContainer(object sender)
        => VisualTreeHelpers.FindListBoxItemContainer(sender);
}
