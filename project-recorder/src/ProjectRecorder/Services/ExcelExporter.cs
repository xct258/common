using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 极简 xlsx 导出（无第三方依赖）：一个工作表 = 一天的「汇总 + 明细」。
/// 批量导出时一个文件内按日期生成多个工作表；工序顺序与工作量界面一致
/// （项目按左侧顺序，工序按 Order → CreatedTime）。
/// 表格带标题、分节标题、表头填色、斑马纹、边框、筛选和打印适配。
/// xlsx 本质是 zip 包，这里用 inline 字符串直写，避免 sharedStrings 表。
/// </summary>
public static class ExcelExporter
{
    private sealed class SheetXml
    {
        public string Name = string.Empty;
        public string TabColor = "FF2563EB";
        public string Xml = string.Empty;
    }

    private static readonly string[] TabColors =
    {
        "FF2563EB", "FF0EA5E9", "FF10B981", "FFF59E0B", "FF8B5CF6", "FFEF4444"
    };

    // ---------- 单日导出：一个工作表（当日汇总 + 明细） ----------

    public static int ExportWorkload(
        string path,
        DateTime day,
        List<WorkloadRecord> records,
        string? shift = null,
        IReadOnlyList<WorkloadProcess>? processes = null,
        IReadOnlyList<string>? projectOrder = null)
    {
        var processList = processes ?? Array.Empty<WorkloadProcess>();
        var list = records
            .Where(x => x.WorkDate.Date == day.Date)
            .Where(x => shift == null || x.Shift == shift)
            .ToList();

        var sheet = BuildDaySheet(day, shift, list, processList, projectOrder);
        WriteWorkbook(path, new[] { sheet });
        return WorkloadExportBuilder.BuildSummary(list, processList, projectOrder).Count;
    }

    // ---------- 批量导出：一个文件，每天一个工作表 ----------

    public static int ExportWorkloadBatch(
        string path,
        DateTime start,
        DateTime end,
        string? shift,
        List<WorkloadRecord> records,
        IReadOnlyList<WorkloadProcess>? processes = null,
        IReadOnlyList<string>? projectOrder = null)
    {
        DateTime from = start.Date;
        DateTime to = end.Date;
        if (from > to) (from, to) = (to, from);

        var filtered = records
            .Where(x => x.WorkDate.Date >= from && x.WorkDate.Date <= to)
            .Where(x => shift == null || x.Shift == shift)
            .ToList();

        var processList = processes ?? Array.Empty<WorkloadProcess>();
        var sheets = new List<SheetXml>();
        foreach (var group in filtered.GroupBy(x => x.WorkDate.Date).OrderBy(g => g.Key))
        {
            var sheet = BuildDaySheet(group.Key, shift, group.ToList(), processList, projectOrder);
            sheet.TabColor = TabColors[sheets.Count % TabColors.Length];
            sheets.Add(sheet);
        }

        if (sheets.Count == 0)
            throw new InvalidOperationException("没有可导出的工作量记录。");

        WriteWorkbook(path, sheets);
        return WorkloadExportBuilder.BuildSummary(filtered, processList, projectOrder).Count;
    }

    // ---------- 单个“日工作表” ----------

    private static SheetXml BuildDaySheet(
        DateTime day,
        string? shift,
        List<WorkloadRecord> records,
        IReadOnlyList<WorkloadProcess> processes,
        IReadOnlyList<string>? projectOrder)
    {
        var summary = WorkloadExportBuilder.BuildSummary(records, processes, projectOrder);
        var details = WorkloadExportBuilder.OrderDetails(records, processes, projectOrder);

        string title = $"工作量统计（{day:yyyy-MM-dd}{(string.IsNullOrEmpty(shift) ? "" : " " + shift)}）";
        return new SheetXml
        {
            Name = day.ToString("yyyy-MM-dd"),
            Xml = BuildDaySheetXml(title, summary, details)
        };
    }

    private static string BuildDaySheetXml(
        string title,
        List<WorkloadSummaryRow> summary,
        List<WorkloadRecord> details)
    {
        double[] widths = CalcDayWidths(title, summary, details);
        var merges = new List<string>
        {
            "A1:E1", // 标题
            "A2:E2"  // 一、汇总
        };

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        int row = 1;

        // 标题
        sb.Append($"<row r=\"{row}\" ht=\"30\" customHeight=\"1\">");
        sb.Append(TextCell("A1", title, 1));
        sb.Append("</row>");
        row++;

        // 一、汇总
        sb.Append($"<row r=\"{row}\" ht=\"20\" customHeight=\"1\">");
        sb.Append(TextCell($"A{row}", "一、汇总", 9));
        sb.Append(TextCell($"B{row}", string.Empty, 9));
        sb.Append(TextCell($"C{row}", string.Empty, 9));
        sb.Append(TextCell($"D{row}", string.Empty, 9));
        sb.Append(TextCell($"E{row}", string.Empty, 9));
        sb.Append("</row>");
        row++;

        // 汇总表头（项目 | 工序跨 B:D | 数量）
        sb.Append($"<row r=\"{row}\" ht=\"22\" customHeight=\"1\">");
        sb.Append(TextCell($"A{row}", "项目", 2));
        sb.Append(TextCell($"B{row}", "工序", 2));
        sb.Append(TextCell($"C{row}", string.Empty, 2));
        sb.Append(TextCell($"D{row}", string.Empty, 2));
        sb.Append(TextCell($"E{row}", "数量", 2));
        sb.Append("</row>");
        merges.Add($"B{row}:D{row}");
        row++;

        // 汇总数据：项目纵向合并，工序横向占用 B:D，数量放 E 列，铺满整张表宽
        int i = 0;
        bool shade = false;
        while (i < summary.Count)
        {
            int j = i;
            while (j + 1 < summary.Count &&
                   string.Equals(summary[j + 1].ProjectName, summary[i].ProjectName, StringComparison.Ordinal))
                j++;

            int groupStart = row;
            for (int k = i; k <= j; k++)
            {
                sb.Append($"<row r=\"{row}\" ht=\"20\" customHeight=\"1\">");
                sb.Append(TextCell($"A{row}", k == i ? summary[k].ProjectName : string.Empty, shade ? 5 : 3));
                sb.Append(TextCell($"B{row}", summary[k].ProcessName, shade ? 5 : 3));
                sb.Append(TextCell($"C{row}", string.Empty, shade ? 5 : 3));
                sb.Append(TextCell($"D{row}", string.Empty, shade ? 5 : 3));
                sb.Append(NumCell($"E{row}", summary[k].Quantity, shade ? 6 : 4));
                sb.Append("</row>");
                merges.Add($"B{row}:D{row}");
                row++;
            }
            if (j > i) merges.Add($"A{groupStart}:A{row - 1}");

            i = j + 1;
            shade = !shade;
        }

        // 间隔行
        sb.Append($"<row r=\"{row}\" ht=\"10\"/>");
        row++;

        // 二、明细
        sb.Append($"<row r=\"{row}\" ht=\"20\" customHeight=\"1\">");
        sb.Append(TextCell($"A{row}", "二、明细（每次执行）", 9));
        sb.Append(TextCell($"B{row}", string.Empty, 9));
        sb.Append(TextCell($"C{row}", string.Empty, 9));
        sb.Append(TextCell($"D{row}", string.Empty, 9));
        sb.Append(TextCell($"E{row}", string.Empty, 9));
        sb.Append("</row>");
        merges.Add($"A{row}:E{row}");
        row++;

        // 明细表头
        int detailHeaderRow = row;
        sb.Append($"<row r=\"{row}\" ht=\"22\" customHeight=\"1\">");
        sb.Append(TextCell($"A{row}", "项目", 2));
        sb.Append(TextCell($"B{row}", "工序", 2));
        sb.Append(TextCell($"C{row}", "班次", 2));
        sb.Append(TextCell($"D{row}", "数量", 2));
        sb.Append(TextCell($"E{row}", "执行时间", 2));
        sb.Append("</row>");
        row++;

        // 明细数据
        for (int k = 0; k < details.Count; k++)
        {
            var d = details[k];
            bool shaded = k % 2 == 1;
            sb.Append($"<row r=\"{row}\" ht=\"20\" customHeight=\"1\">");
            sb.Append(TextCell($"A{row}", d.ProjectName ?? string.Empty, shaded ? 5 : 3));
            sb.Append(TextCell($"B{row}", d.ProcessName ?? string.Empty, shaded ? 5 : 3));
            sb.Append(TextCell($"C{row}", d.Shift ?? string.Empty, shaded ? 5 : 3));
            sb.Append(NumCell($"D{row}", d.Quantity, shaded ? 6 : 4));
            sb.Append(TextCell($"E{row}", FormatDetailTime(d), shaded ? 5 : 3));
            sb.Append("</row>");
            row++;
        }
        int lastRow = row - 1;

        sb.Append("</sheetData>");

        // 明细表筛选
        if (details.Count > 0)
            sb.Append($"<autoFilter ref=\"A{detailHeaderRow}:E{lastRow}\"/>");

        if (merges.Count > 0)
        {
            sb.Append($"<mergeCells count=\"{merges.Count}\">");
            foreach (string m in merges) sb.Append($"<mergeCell ref=\"{m}\"/>");
            sb.Append("</mergeCells>");
        }

        sb.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");

        // 项目/工序可能是纯数字名称：保留文本原样（不丢前导 0），同时抑制 Excel 的“数字以文本形式存储”提示
        if (details.Count > 0 || summary.Count > 0)
            sb.Append($"<ignoredErrors><ignoredError sqref=\"A4:E{lastRow}\" numberStoredAsText=\"1\"/></ignoredErrors>");

        sb.Append("</worksheet>");
        return BuildSheetWrapper(sb.ToString(), lastRow, widths);
    }

    // 把 sheetData 前后补齐（列宽依赖内容，必须在拼完数据后计算）
    private static string BuildSheetWrapper(string body, int lastRow, double[] widths)
    {
        // body 以 XML 声明开头，插入 dimension/sheetViews/cols 到 sheetPr 之后
        const string marker = "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>";
        int idx = body.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return body;
        idx += marker.Length;

        var prefix = new StringBuilder();
        prefix.Append($"<dimension ref=\"A1:E{lastRow}\"/>");
        prefix.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        prefix.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        prefix.Append("</sheetView></sheetViews>");
        prefix.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        prefix.Append("<cols>");
        for (int c = 0; c < widths.Length; c++)
            prefix.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c].ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        prefix.Append("</cols>");
        prefix.Append("<sheetData>");

        return body.Insert(idx, prefix.ToString());
    }

    private static double[] CalcDayWidths(
        string title,
        List<WorkloadSummaryRow> summary,
        List<WorkloadRecord> details)
    {
        double[] w =
        {
            DisplayWidth("项目"),
            DisplayWidth("工序"),
            DisplayWidth("班次"),
            DisplayWidth("数量"),
            DisplayWidth("执行时间")
        };

        foreach (var s in summary)
        {
            w[0] = Math.Max(w[0], DisplayWidth(s.ProjectName));
            w[1] = Math.Max(w[1], DisplayWidth(s.ProcessName));
            w[4] = Math.Max(w[4], DisplayWidth(s.Quantity.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var d in details)
        {
            w[0] = Math.Max(w[0], DisplayWidth(d.ProjectName));
            w[1] = Math.Max(w[1], DisplayWidth(d.ProcessName));
            w[2] = Math.Max(w[2], DisplayWidth(d.Shift));
            w[3] = Math.Max(w[3], DisplayWidth(d.Quantity.ToString(CultureInfo.InvariantCulture)));
            w[4] = Math.Max(w[4], DisplayWidth(FormatDetailTime(d)));
        }

        double[] caps = { 80, 60, 10, 10, 12 };
        for (int c = 0; c < w.Length; c++)
            w[c] = Math.Min(w[c] + 2, caps[c]);

        // 标题是 16 号字，列宽单位按 11 号字计算：先按字号放大，再留出单元格内边距
        double titleWidth = DisplayWidth(title) * (16.0 / 11.0) + 8;
        double totalWidth = w.Sum();
        if (totalWidth < titleWidth)
        {
            double add = (titleWidth - totalWidth) / w.Length + 0.1;
            for (int c = 0; c < w.Length; c++) w[c] += add;
        }
        return w;
    }

    // ---------- 工作簿打包 ----------

    private static void WriteWorkbook(string path, IReadOnlyList<SheetXml> sheets)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

        var contentTypes = new StringBuilder();
        contentTypes.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        contentTypes.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        contentTypes.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        contentTypes.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        contentTypes.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        contentTypes.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < sheets.Count; i++)
            contentTypes.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        contentTypes.Append("</Types>");
        WriteEntry(zip, "[Content_Types].xml", contentTypes.ToString());

        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        var workbook = new StringBuilder();
        workbook.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        workbook.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        workbook.Append("<sheets>");
        for (int i = 0; i < sheets.Count; i++)
            workbook.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        workbook.Append("</sheets></workbook>");
        WriteEntry(zip, "xl/workbook.xml", workbook.ToString());

        var rels = new StringBuilder();
        rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheets.Count; i++)
            rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        rels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        rels.Append("</Relationships>");
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

        WriteEntry(zip, "xl/styles.xml", BuildStyles());

        for (int i = 0; i < sheets.Count; i++)
        {
            // 每个工作表可指定标签颜色：在 sheetPr 中插入 tabColor
            string xml = sheets[i].Xml;
            xml = xml.Replace(
                "<sheetPr><pageSetUpPr",
                $"<sheetPr><tabColor rgb=\"{sheets[i].TabColor}\"/><pageSetUpPr");
            WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", xml);
        }
    }

    // ---------- 样式 ----------

    private static string BuildStyles()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"5\">" +
            "<font><sz val=\"11\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"16\"/><color rgb=\"FF1F4E79\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"12\"/><color rgb=\"FF1F4E79\"/><name val=\"等线\"/></font>" +
            "</fonts>" +
            "<fills count=\"5\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2563EB\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8FAFC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDBEAFE\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border>" +
            "<left style=\"thin\"><color rgb=\"FF000000\"/></left>" +
            "<right style=\"thin\"><color rgb=\"FF000000\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FF000000\"/></top>" +
            "<bottom style=\"thin\"><color rgb=\"FF000000\"/></bottom>" +
            "<diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"10\">" +
            // 0 默认
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            // 1 标题
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 2 表头
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            // 3 正文
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            // 4 数字
            "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 5 斑马正文
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            // 6 斑马数字
            "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 7 合计文字
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 8 合计数字
            "<xf numFmtId=\"1\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 9 分节标题
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
            "</cellXfs>" +
            "</styleSheet>";
    }

    // ---------- 通用工具 ----------

    private static string TextCell(string reference, string? text, int style)
        => $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

    // 每个工作表就是一天，明细只显示时间；补录仍显示“补录”
    private static string FormatDetailTime(WorkloadRecord record)
        => record.IsBackfill ? "补录" : record.CreatedTime.ToString("HH:mm:ss");

    private static string NumCell(string reference, int value, int style)
        => $"<c r=\"{reference}\" s=\"{style}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>";

    // 估算文本显示宽度：半角 1，全角/中文 2
    private static double DisplayWidth(string? s)
    {
        double w = 0;
        foreach (char c in s ?? string.Empty)
            w += c <= 0x7F ? 1 : 2;
        return w;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
    }
}
