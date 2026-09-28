using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 极简 xlsx 导出（无第三方依赖）：表一为 项目/工序 数量合计（冻结前两行），
/// 表二为每次执行的明细（项目/工序/日期/班次/数量/执行时间，按时间排序）。
/// 每条工作量记录的 CreatedTime 即该次执行时间。
/// xlsx 本质是 zip 包，这里用 inline 字符串直写，避免 sharedStrings 表。
/// </summary>
public static class ExcelExporter
{
    public static int ExportWorkload(string path, DateTime day, List<WorkloadRecord> records, string? shift = null)
    {
        // 按 项目 + 工序 汇总，只保留总数
        var totals = records
            .GroupBy(x => new { x.ProjectName, x.ProcessName })
            .Select(g => new
            {
                g.Key.ProjectName,
                g.Key.ProcessName,
                Quantity = g.Sum(x => x.Quantity)
            })
            .OrderBy(x => x.ProjectName)
            .ThenBy(x => x.ProcessName)
            .ToList();

        string title = shift == null
            ? $"工作量统计（{day:yyyy-MM-dd}）"
            : $"工作量统计（{day:yyyy-MM-dd} {shift}）";

        // 明细：每次执行一条，按执行时间排序（含负数冲减记录）
        var details = records
            .OrderBy(x => x.CreatedTime)
            .ThenBy(x => x.ProjectName)
            .ThenBy(x => x.ProcessName)
            .ToList();

        // 根据表头与内容自动计算列宽（中文按 2 个字符宽估算）
        double wProject = DisplayWidth("项目"), wProcess = DisplayWidth("工序"), wQty = DisplayWidth("数量");
        foreach (var t in totals)
        {
            wProject = Math.Max(wProject, DisplayWidth(t.ProjectName));
            wProcess = Math.Max(wProcess, DisplayWidth(t.ProcessName));
            wQty = Math.Max(wQty, DisplayWidth(t.Quantity.ToString(CultureInfo.InvariantCulture)));
        }
        wProject = Math.Min(wProject + 2, 50);
        wProcess = Math.Min(wProcess + 2, 50);
        wQty = Math.Min(wQty + 2, 12);

        // 标题在 A1:C1 合并显示，三列总宽不够会被 Excel 截断：按需把缺口均摊到三列（多给 0.3 防四舍五入后仍差一点）
        double titleWidth = DisplayWidth(title) + 2;
        double totalWidth = wProject + wProcess + wQty;
        if (totalWidth < titleWidth)
        {
            double add = (titleWidth - totalWidth) / 3.0 + 0.1;
            wProject += add;
            wProcess += add;
            wQty += add;
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

        WriteEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");

        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        WriteEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"工作量\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"明细\" sheetId=\"2\" r:id=\"rId3\"/></sheets>" +
            "</workbook>");

        WriteEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
            "</Relationships>");

        WriteEntry(zip, "xl/styles.xml", BuildStyles());

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        sb.Append("<pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        sb.Append("</sheetView></sheetViews>");
        var merges = new List<string> { "A1:C1" };
        sb.Append("<cols>");
        sb.Append($"<col min=\"1\" max=\"1\" width=\"{wProject.ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append($"<col min=\"2\" max=\"2\" width=\"{wProcess.ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append($"<col min=\"3\" max=\"3\" width=\"{wQty.ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append("</cols><sheetData>");
        sb.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>");
        sb.Append(Escape(title));
        sb.Append("</t></is></c></row>");
        sb.Append("<row r=\"2\">");
        sb.Append(CellInline("A2", "项目", 2));
        sb.Append(CellInline("B2", "工序", 2));
        sb.Append(CellInline("C2", "数量", 2));
        sb.Append("</row>");
        for (int i = 0; i < totals.Count; i++)
        {
            var r = totals[i];
            int row = i + 3;
            sb.Append($"<row r=\"{row}\">");
            // 同一项目的项目名只在首行写一次，其余行合并到该单元格
            if (i == 0 || totals[i - 1].ProjectName != r.ProjectName)
            {
                int j = i;
                while (j + 1 < totals.Count && totals[j + 1].ProjectName == r.ProjectName) j++;
                if (j > i) merges.Add($"A{row}:A{j + 3}");
                sb.Append(CellInline($"A{row}", r.ProjectName, 3));
            }
            sb.Append(CellInline($"B{row}", r.ProcessName, 3));
            sb.Append($"<c r=\"C{row}\" s=\"3\"><v>{r.Quantity}</v></c>");
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (merges.Count > 0)
        {
            sb.Append($"<mergeCells count=\"{merges.Count}\">");
            foreach (string m in merges) sb.Append($"<mergeCell ref=\"{m}\"/>");
            sb.Append("</mergeCells>");
        }
        sb.Append("</worksheet>");
        WriteEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());

        WriteEntry(zip, "xl/worksheets/sheet2.xml", BuildDetailSheet(title, details));
        return totals.Count;
    }

    // 表二：每次执行的明细（项目/工序/日期/班次/数量/执行时间），同样标题 + 表头 + 冻结前两行
    private static string BuildDetailSheet(string title, List<WorkloadRecord> details)
    {
        string[] headers = { "项目", "工序", "日期", "班次", "数量", "执行时间" };
        string[] widths = new string[headers.Length];
        double[] w = new double[headers.Length];
        for (int c = 0; c < headers.Length; c++) w[c] = DisplayWidth(headers[c]);
        var rows = new List<string[]>();
        foreach (var d in details)
        {
            string[] cells =
            {
                d.ProjectName,
                d.ProcessName,
                d.WorkDate.ToString("yyyy-MM-dd"),
                d.Shift,
                d.Quantity.ToString(CultureInfo.InvariantCulture),
                d.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss")
            };
            rows.Add(cells);
            for (int c = 0; c < cells.Length; c++) w[c] = Math.Max(w[c], DisplayWidth(cells[c]));
        }
        double[] caps = { 50, 50, 14, 10, 12, 22 };
        for (int c = 0; c < w.Length; c++)
        {
            w[c] = Math.Min(w[c] + 2, caps[c]);
            widths[c] = w[c].ToString("0.#", CultureInfo.InvariantCulture);
        }

        string detailTitle = title + "（执行明细）";
        double titleWidth = DisplayWidth(detailTitle) + 2;
        double totalWidth = w.Sum();
        if (totalWidth < titleWidth)
        {
            double add = (titleWidth - totalWidth) / w.Length + 0.1;
            for (int c = 0; c < w.Length; c++)
            {
                w[c] += add;
                widths[c] = w[c].ToString("0.#", CultureInfo.InvariantCulture);
            }
        }

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        sb.Append("<pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        sb.Append("</sheetView></sheetViews>");
        sb.Append("<cols>");
        for (int c = 0; c < widths.Length; c++)
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c]}\" customWidth=\"1\"/>");
        sb.Append("</cols><sheetData>");
        sb.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>");
        sb.Append(Escape(detailTitle));
        sb.Append("</t></is></c></row>");
        sb.Append("<row r=\"2\">");
        for (int c = 0; c < headers.Length; c++)
            sb.Append(CellInline($"{(char)('A' + c)}2", headers[c], 2));
        sb.Append("</row>");
        for (int i = 0; i < rows.Count; i++)
        {
            int row = i + 3;
            sb.Append($"<row r=\"{row}\">");
            for (int c = 0; c < rows[i].Length; c++)
            {
                string cell = $"{(char)('A' + c)}{row}";
                if (c == 4)
                    sb.Append($"<c r=\"{cell}\" s=\"3\"><v>{Escape(rows[i][c])}</v></c>");
                else
                    sb.Append(CellInline(cell, rows[i][c], 3));
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        sb.Append("<mergeCells count=\"1\"><mergeCell ref=\"A1:F1\"/></mergeCells>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static string CellInline(string @ref, string text, int style)
        => $"<c r=\"{@ref}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

    // 估算文本显示宽度：半角 1，全角/中文 2
    private static double DisplayWidth(string? s)
    {
        double w = 0;
        foreach (char c in s ?? string.Empty)
            w += c <= 0x7F ? 1 : 2;
        return w;
    }

    private static string BuildStyles()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"3\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"等线\"/></font>" +
            "</fonts>" +
            "<fills count=\"3\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDBEAFE\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"4\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\">" +
            "<alignment horizontal=\"center\" vertical=\"center\"/>" +
            "</xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\">" +
            "<alignment vertical=\"center\"/>" +
            "</xf>" +
            "</cellXfs>" +
            "</styleSheet>";
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return SecurityElement.Escape(s) ?? string.Empty;
    }
}
