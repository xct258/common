using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 极简 xlsx 导出（无第三方依赖）：标题 + 表头 + 明细，冻结前两行。
/// xlsx 本质是 zip 包，这里用 inline 字符串直写，避免 sharedStrings 表。
/// </summary>
public static class ExcelExporter
{
    public static void ExportWorkload(string path, DateTime day, List<WorkloadRecord> records)
    {
        var rows = records
            .OrderBy(x => x.ProjectName)
            .ThenBy(x => x.ProcessName)
            .ThenBy(x => x.CreatedTime)
            .ToList();

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

        WriteEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
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
            "<sheets><sheet name=\"工作量\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>");

        WriteEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");

        WriteEntry(zip, "xl/styles.xml", BuildStyles());

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        sb.Append("<pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        sb.Append("</sheetView></sheetViews>");
        sb.Append("<cols>");
        sb.Append("<col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/>");
        sb.Append("<col min=\"2\" max=\"2\" width=\"32\" customWidth=\"1\"/>");
        sb.Append("<col min=\"3\" max=\"3\" width=\"12\" customWidth=\"1\"/>");
        sb.Append("<col min=\"4\" max=\"4\" width=\"12\" customWidth=\"1\"/>");
        sb.Append("</cols><sheetData>");
        sb.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>");
        sb.Append(Escape($"工作量统计（{day:yyyy-MM-dd}）"));
        sb.Append("</t></is></c></row>");
        sb.Append("<row r=\"2\">");
        sb.Append(CellInline("A2", "项目", 2));
        sb.Append(CellInline("B2", "工序", 2));
        sb.Append(CellInline("C2", "班次", 2));
        sb.Append(CellInline("D2", "数量", 2));
        sb.Append("</row>");
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 3;
            sb.Append($"<row r=\"{row}\">");
            sb.Append(CellInline($"A{row}", r.ProjectName, 0));
            sb.Append(CellInline($"B{row}", r.ProcessName, 0));
            sb.Append(CellInline($"C{row}", r.Shift, 0));
            sb.Append($"<c r=\"D{row}\" s=\"0\"><v>{r.Quantity}</v></c>");
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        WriteEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
    }

    private static string CellInline(string @ref, string text, int style)
        => $"<c r=\"{@ref}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

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
            "<cellXfs count=\"3\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\">" +
            "<alignment horizontal=\"center\" vertical=\"center\"/>" +
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
