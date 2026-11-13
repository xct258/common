using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ProjectRecorder.Panels;

/// <summary>
/// 靠左自适应流式面板：卡片固定大小，每行从左起排。
/// 排满的行：剩余宽度全部平均分配给卡片之间的缝隙（仅保底 MinGap，不封顶），恰好填满整行；
/// 没排满的末行：按排满行的列缝隙从左到右紧排，与上面的列对齐；没有排满行时按 MinGap 紧排。
/// 每行张数随窗口宽度自动增减。
/// 注意：卡片模板固定 Width，左右边距为 0（Margin="0,8"），水平间距完全由本面板控制。
/// 用于项目卡片 / 工序卡片 / 工作量项目卡片三处列表。
/// </summary>
public class AdaptiveWrapPanel : Panel
{
    /// <summary>卡片之间最小缝隙（默认 16）。实际缝隙只会比它大，不会比它小。</summary>
    public double MinGap { get; set; } = 16;

    protected override Size MeasureOverride(Size availableSize)
    {
        double rowWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(rowWidth, double.PositiveInfinity));

        double totalH = 0, curW = 0, curH = 0;
        bool first = true;
        foreach (UIElement child in InternalChildren)
        {
            double w = child.DesiredSize.Width;
            double need = w + (first ? 0 : MinGap);
            if (rowWidth > 0 && !first && curW + need > rowWidth)
            {
                totalH += curH;
                curW = 0;
                curH = 0;
                first = true;
                need = w;
            }
            curW += need;
            first = false;
            if (child.DesiredSize.Height > curH) curH = child.DesiredSize.Height;
        }
        totalH += curH;

        double rw = double.IsInfinity(availableSize.Width) ? curW : availableSize.Width;
        return new Size(rw, totalH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = new List<List<UIElement>>();
        var cur = new List<UIElement>();
        double curW = 0;
        foreach (UIElement child in InternalChildren)
        {
            double need = child.DesiredSize.Width + (cur.Count == 0 ? 0 : MinGap);
            if (cur.Count > 0 && curW + need > finalSize.Width)
            {
                rows.Add(cur);
                cur = new List<UIElement>();
                curW = 0;
                need = child.DesiredSize.Width;
            }
            cur.Add(child);
            curW += need;
        }
        if (cur.Count > 0) rows.Add(cur);

        double y = 0;
        int m = rows.Count;
        var info = new List<(List<UIElement> row, double used, double h, bool full)>(m);
        for (int i = 0; i < m; i++)
        {
            var row = rows[i];
            double used = 0, h = 0, maxW = 0;
            foreach (var c in row)
            {
                used += c.DesiredSize.Width;
                if (c.DesiredSize.Height > h) h = c.DesiredSize.Height;
                if (c.DesiredSize.Width > maxW) maxW = c.DesiredSize.Width;
            }
            // 非末行一定是被宽度截断的排满行；末行用“再来一张同宽卡片放不下”来判定是否排满
            bool full = i < m - 1 || used + MinGap + maxW > finalSize.Width;
            info.Add((row, used, h, full));
        }
        // 列标准缝隙：取第一个排满行的填充缝隙；没有排满行则用 MinGap
        double refGap = MinGap;
        foreach (var (row, used, h, full) in info)
        {
            if (full && row.Count > 1)
            {
                refGap = (finalSize.Width - used) / (row.Count - 1);
                if (refGap < MinGap) refGap = MinGap;
                break;
            }
        }
        foreach (var (row, used, h, full) in info)
        {
            double gap;
            if (full && row.Count > 1)
            {
                gap = (finalSize.Width - used) / (row.Count - 1);
                if (gap < MinGap) gap = MinGap;
            }
            else
            {
                gap = refGap;
            }
            double x = 0;
            foreach (var c in row)
            {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, h));
                x += c.DesiredSize.Width + gap;
            }
            y += h;
        }
        return finalSize;
    }
}
