using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ProjectRecorder.Models;
using ProjectRecorder.Services;
using Xunit;

namespace ProjectRecorder.Core.Tests;

public class WorkloadExportBuilderTests
{
    private static WorkloadProcess Proc(string project, string process, int order, DateTime? created = null)
        => new()
        {
            ProjectName = project,
            ProcessName = process,
            Order = order,
            CreatedTime = created ?? new DateTime(2026, 1, 1)
        };

    private static WorkloadRecord Rec(
        string project, string process, DateTime date, string shift, int qty,
        DateTime? created = null)
        => new()
        {
            ProjectName = project,
            ProcessName = process,
            WorkDate = date,
            Shift = shift,
            Quantity = qty,
            CreatedTime = created ?? date.AddHours(9)
        };

    [Fact]
    public void Summary_FollowsUiProcessOrder_NotAlphabetical()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1),
            Proc("P", "丙", 3)
        };
        var records = new[]
        {
            Rec("P", "丙", DateTime.Today, WorkloadRecord.DayShift, 3),
            Rec("P", "甲", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("P", "乙", DateTime.Today, WorkloadRecord.DayShift, 2)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal(new[] { "甲", "乙", "丙" }, rows.Select(x => x.ProcessName));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(x => x.Quantity));
    }

    [Fact]
    public void Summary_TieBreaksByCreatedTime()
    {
        var t = new DateTime(2026, 1, 1);
        var processes = new[]
        {
            Proc("P", "后建", 1, t.AddDays(1)),
            Proc("P", "先建", 1, t)
        };
        var records = new[]
        {
            Rec("P", "后建", t, WorkloadRecord.DayShift, 1),
            Rec("P", "先建", t, WorkloadRecord.DayShift, 1)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal(new[] { "先建", "后建" }, rows.Select(x => x.ProcessName));
    }

    [Fact]
    public void Summary_FollowsProjectOrderFromUi()
    {
        var processes = new[]
        {
            Proc("A", "工序", 1),
            Proc("B", "工序", 1),
            Proc("通用", "工序", 1)
        };
        var records = new[]
        {
            Rec("A", "工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("B", "工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("通用", "工序", DateTime.Today, WorkloadRecord.DayShift, 1)
        };

        var rows = WorkloadExportBuilder.BuildSummary(
            records, processes, new[] { "通用", "B", "A" });

        Assert.Equal(new[] { "通用", "B", "A" }, rows.Select(x => x.ProjectName));
    }

    [Fact]
    public void Summary_UnknownProcessGoesLast()
    {
        var processes = new[] { Proc("P", "已登记", 1) };
        var records = new[]
        {
            Rec("P", "已删除旧工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("P", "已登记", DateTime.Today, WorkloadRecord.DayShift, 2)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal("已登记", rows[0].ProcessName);
        Assert.Equal("已删除旧工序", rows[1].ProcessName);
    }

    [Fact]
    public void Daily_GroupsByDateShiftAndKeepsUiOrder()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1)
        };
        var d1 = new DateTime(2026, 10, 20);
        var d2 = new DateTime(2026, 10, 21);
        var records = new[]
        {
            Rec("P", "乙", d2, WorkloadRecord.DayShift, 2),
            Rec("P", "甲", d2, WorkloadRecord.DayShift, 1),
            Rec("P", "乙", d2, WorkloadRecord.DayShift, 3),
            Rec("P", "乙", d1, WorkloadRecord.NightShift, 5)
        };

        var rows = WorkloadExportBuilder.BuildDaily(records, processes, new[] { "P" });

        Assert.Equal(3, rows.Count);
        Assert.Equal(d1, rows[0].WorkDate);
        Assert.Equal(5, rows[0].Quantity);
        Assert.Equal(d2, rows[1].WorkDate);
        Assert.Equal("甲", rows[1].ProcessName);
        Assert.Equal(1, rows[1].Quantity);
        Assert.Equal("乙", rows[2].ProcessName);
        Assert.Equal(5, rows[2].Quantity);
    }

    [Fact]
    public void Details_FollowUiOrderWithinDate()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1)
        };
        var d = new DateTime(2026, 10, 22);
        var records = new[]
        {
            Rec("P", "乙", d, WorkloadRecord.DayShift, 1, d.AddHours(10)),
            Rec("P", "甲", d, WorkloadRecord.DayShift, 1, d.AddHours(11))
        };

        var ordered = WorkloadExportBuilder.OrderDetails(records, processes, new[] { "P" });

        Assert.Equal(new[] { "甲", "乙" }, ordered.Select(x => x.ProcessName));
    }
}

public class ExcelExporterTests
{
    private static readonly XNamespace Ns =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? CellValue(XDocument sheet, string reference)
    {
        var cell = sheet.Descendants(Ns + "c")
            .FirstOrDefault(c => (string?)c.Attribute("r") == reference);
        if (cell == null) return null;
        return cell.Element(Ns + "v")?.Value
               ?? cell.Descendants(Ns + "t").FirstOrDefault()?.Value;
    }

    private static List<string> SheetNames(ZipArchive zip)
    {
        var wb = XDocument.Parse(ReadEntry(zip, "xl/workbook.xml"));
        return wb.Descendants(Ns + "sheet")
            .Select(x => (string?)x.Attribute("name") ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void ExportWorkload_WritesStyledWorkbook_InUiOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_export_{Guid.NewGuid():N}.xlsx");
        try
        {
            var processes = new[]
            {
                new WorkloadProcess { ProjectName = "P", ProcessName = "乙", Order = 2 },
                new WorkloadProcess { ProjectName = "P", ProcessName = "甲", Order = 1 }
            };
            var day = new DateTime(2026, 10, 22);
            var records = new List<WorkloadRecord>
            {
                new() { ProjectName = "P", ProcessName = "乙", WorkDate = day, Shift = WorkloadRecord.DayShift, Quantity = 2 },
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = day, Shift = WorkloadRecord.DayShift, Quantity = 1 }
            };

            int count = ExcelExporter.ExportWorkload(
                path, day, records, null, processes, new[] { "P" });

            Assert.Equal(2, count);
            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("xl/styles.xml"));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
            Assert.Null(zip.GetEntry("xl/worksheets/sheet2.xml"));

            var styles = XDocument.Parse(ReadEntry(zip, "xl/styles.xml"));
            var cellXfs = styles.Descendants(Ns + "cellXfs").First();
            Assert.Equal("10", (string?)cellXfs.Attribute("count"));

            var sheet1 = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));
            Assert.Equal("一、汇总", CellValue(sheet1, "A2"));
            Assert.Equal("工序", CellValue(sheet1, "B3"));
            Assert.Equal("数量", CellValue(sheet1, "E3"));
            Assert.Equal("甲", CellValue(sheet1, "B4"));
            Assert.Equal("乙", CellValue(sheet1, "B5"));
            Assert.Equal("1", CellValue(sheet1, "E4"));
            Assert.Equal("", CellValue(sheet1, "C4")); // B:D 合并区内的空单元格也带边框样式
            Assert.Equal("", CellValue(sheet1, "D4"));
            Assert.Null(CellValue(sheet1, "C6")); // 不再生成“合计”行
            Assert.Equal("二、明细（每次执行）", CellValue(sheet1, "A7"));
            Assert.Equal("甲", CellValue(sheet1, "B9"));
            Assert.Equal("乙", CellValue(sheet1, "B10"));
            Assert.DoesNotContain("合计", sheet1.Descendants(Ns + "t").Select(t => t.Value));
            Assert.NotNull(sheet1.Descendants(Ns + "pane").FirstOrDefault());
            Assert.NotNull(sheet1.Descendants(Ns + "autoFilter").FirstOrDefault());
            Assert.NotNull(sheet1.Descendants(Ns + "mergeCell")
                .FirstOrDefault(x => (string?)x.Attribute("ref") == "A1:E1"));
            Assert.NotNull(sheet1.Descendants(Ns + "mergeCell")
                .FirstOrDefault(x => (string?)x.Attribute("ref") == "B3:D3"));
            Assert.NotNull(sheet1.Descendants(Ns + "tabColor").FirstOrDefault());

            // 表格使用黑色细边框
            var border = styles.Descendants(Ns + "border").Skip(1).First();
            foreach (var edge in border.Elements().Where(x => x.Name.LocalName != "diagonal"))
            {
                Assert.Equal("thin", (string?)edge.Attribute("style"));
                Assert.Equal("FF000000", (string?)edge.Element(Ns + "color")?.Attribute("rgb"));
            }

            var ignored = sheet1.Descendants(Ns + "ignoredError").FirstOrDefault();
            Assert.NotNull(ignored);
            Assert.Equal("1", (string?)ignored!.Attribute("numberStoredAsText"));

            // 标题用 16 号字，列宽总和必须覆盖标题实际宽度，避免打开就被截断
            double totalWidth = sheet1.Descendants(Ns + "col")
                .Select(c => double.Parse((string)c.Attribute("width")!, System.Globalization.CultureInfo.InvariantCulture))
                .Sum();
            Assert.True(totalWidth >= 38.0, $"列宽总和 {totalWidth} 不足以显示标题");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportWorkload_NumericProjectAndProcess_StaysTextWithoutWarning()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_numeric_{Guid.NewGuid():N}.xlsx");
        try
        {
            var processes = new[]
            {
                new WorkloadProcess { ProjectName = "123456", ProcessName = "00123", Order = 1 }
            };
            var day = new DateTime(2026, 10, 22);
            var records = new List<WorkloadRecord>
            {
                new() { ProjectName = "123456", ProcessName = "00123", WorkDate = day, Shift = WorkloadRecord.DayShift, Quantity = 1 }
            };

            ExcelExporter.ExportWorkload(path, day, records, null, processes, new[] { "123456" });

            using var zip = ZipFile.OpenRead(path);
            var sheet1 = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));
            var projectCell = sheet1.Descendants(Ns + "c").First(c => (string?)c.Attribute("r") == "A4");
            var processCell = sheet1.Descendants(Ns + "c").First(c => (string?)c.Attribute("r") == "B4");

            Assert.Equal("inlineStr", (string?)projectCell.Attribute("t"));
            Assert.Equal("inlineStr", (string?)processCell.Attribute("t"));
            Assert.Equal("123456", CellValue(sheet1, "A4"));
            Assert.Equal("00123", CellValue(sheet1, "B4"));
            Assert.NotNull(sheet1.Descendants(Ns + "ignoredError")
                .FirstOrDefault(x => (string?)x.Attribute("numberStoredAsText") == "1"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportWorkload_OtherRecord_ShowsProjectAndTimeAsEntered()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_other_{Guid.NewGuid():N}.xlsx");
        try
        {
            var day = new DateTime(2026, 10, 23);
            var records = new List<WorkloadRecord>
            {
                new()
                {
                    ProjectName = "参加比赛",
                    ProcessName = WorkloadRecord.OtherProcessName,
                    WorkDate = day,
                    Shift = WorkloadRecord.DayShift,
                    Quantity = 2,
                    IsOther = true,
                    CreatedTime = day.AddHours(10)
                }
            };

            int count = ExcelExporter.ExportWorkload(
                path, day, records, null, null, new[] { "通用" });

            Assert.Equal(1, count);
            using var zip = ZipFile.OpenRead(path);
            var sheet = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));

            // 汇总和明细都原样显示：项目=参加比赛，工序=其它，数量/时间=2
            Assert.Equal("参加比赛", CellValue(sheet, "A4"));
            Assert.Equal("其它", CellValue(sheet, "B4"));
            Assert.Equal("2", CellValue(sheet, "E4"));
            Assert.Equal("参加比赛", CellValue(sheet, "A8"));
            Assert.Equal("其它", CellValue(sheet, "B8"));
            Assert.Equal("2", CellValue(sheet, "D8"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportWorkloadBatch_OneSheetPerDay_WithSummaryAndDetailsTogether()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_batch_{Guid.NewGuid():N}.xlsx");
        try
        {
            var processes = new[]
            {
                new WorkloadProcess { ProjectName = "P", ProcessName = "乙", Order = 2 },
                new WorkloadProcess { ProjectName = "P", ProcessName = "甲", Order = 1 }
            };
            var d1 = new DateTime(2026, 10, 20);
            var d2 = new DateTime(2026, 10, 21);
            var records = new List<WorkloadRecord>
            {
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = d1, Shift = WorkloadRecord.DayShift, Quantity = 1, CreatedTime = d1.AddHours(9) },
                new() { ProjectName = "P", ProcessName = "乙", WorkDate = d1, Shift = WorkloadRecord.DayShift, Quantity = 2, CreatedTime = d1.AddHours(10) },
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = d2, Shift = WorkloadRecord.NightShift, Quantity = 3, IsBackfill = true }
            };

            // 结束日期故意多一天（10-22 无数据），应只生成有数据的两个日工作表
            int count = ExcelExporter.ExportWorkloadBatch(
                path, d1, d2.AddDays(1), null, records, processes, new[] { "P" });

            Assert.Equal(2, count);
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(new[] { "2026-10-20", "2026-10-21" }, SheetNames(zip));
            Assert.Null(zip.GetEntry("xl/worksheets/sheet3.xml"));

            // 第一天：汇总 + 明细在同一个工作表
            var day1 = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));
            Assert.Equal("一、汇总", CellValue(day1, "A2"));
            Assert.Equal("甲", CellValue(day1, "B4"));
            Assert.Equal("乙", CellValue(day1, "B5"));
            Assert.Equal("1", CellValue(day1, "E4"));
            Assert.Equal("2", CellValue(day1, "E5"));
            Assert.Equal("二、明细（每次执行）", CellValue(day1, "A7"));
            Assert.Equal("甲", CellValue(day1, "B9"));
            Assert.Equal("09:00:00", CellValue(day1, "E9"));
            Assert.Equal("乙", CellValue(day1, "B10"));
            Assert.DoesNotContain("合计", day1.Descendants(Ns + "t").Select(t => t.Value));

            // 第二天：同样一个工作表，夜班补录明细
            var day2 = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet2.xml"));
            Assert.Equal("一、汇总", CellValue(day2, "A2"));
            Assert.Equal("甲", CellValue(day2, "B4"));
            Assert.Equal("3", CellValue(day2, "E4"));
            Assert.Equal("二、明细（每次执行）", CellValue(day2, "A6"));
            Assert.Equal("甲", CellValue(day2, "B8"));
            Assert.Equal("夜班", CellValue(day2, "C8"));
            Assert.Equal("3", CellValue(day2, "D8"));
            Assert.Equal("补录", CellValue(day2, "E8"));
            Assert.DoesNotContain("合计", day2.Descendants(Ns + "t").Select(t => t.Value));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
