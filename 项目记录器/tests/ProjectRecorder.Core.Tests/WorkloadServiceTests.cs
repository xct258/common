using ProjectRecorder.Models;
using ProjectRecorder.Services;
using Xunit;

namespace ProjectRecorder.Core.Tests;

public class WorkloadServiceTests
{
    private static WorkloadProcess Proc(string name, int order = 1, int createdDay = 1) => new()
    {
        Id = name,
        ProjectName = "P",
        ProcessName = name,
        Order = order,
        CreatedTime = new DateTime(2026, 10, createdDay)
    };

    private static WorkloadRecord Rec(
        string process, int qty, string shift = WorkloadRecord.DayShift,
        int day = 12, int hour = 10, bool backfill = false, int createdDay = 12)
        => new()
        {
            ProjectName = "P",
            ProcessName = process,
            WorkDate = new DateTime(2026, 10, day),
            Shift = shift,
            Quantity = qty,
            IsBackfill = backfill,
            CreatedTime = new DateTime(2026, 10, createdDay, hour, 0, 0)
        };

    [Fact]
    public void ComputeCards_OrdersByOrderThenCreatedTime()
    {
        var procs = new[] { Proc("B", order: 2), Proc("A", order: 1), Proc("C", order: 2, createdDay: 2) };

        var cards = WorkloadService.ComputeCards(procs, Array.Empty<WorkloadRecord>(),
            new DateTime(2026, 10, 12), WorkloadRecord.DayShift);

        Assert.Equal(new[] { "A", "B", "C" }, cards.Select(c => c.Process.ProcessName).ToArray());
    }

    [Fact]
    public void ComputeCards_FiltersByShiftDateAndShiftName()
    {
        var procs = new[] { Proc("A"), Proc("B") };
        var records = new[]
        {
            Rec("A", 2),
            Rec("A", 3, shift: WorkloadRecord.NightShift),      // 班次不符
            Rec("A", 4, day: 13),                                // 日期不符
            Rec("B", 5, shift: WorkloadRecord.NightShift)
        };

        var cards = WorkloadService.ComputeCards(procs, records,
            new DateTime(2026, 10, 12), WorkloadRecord.DayShift);

        Assert.Equal(2, cards.Single(c => c.Process.ProcessName == "A").ShiftQty);
        Assert.Equal(0, cards.Single(c => c.Process.ProcessName == "B").ShiftQty);
    }

    [Fact]
    public void ComputeCards_SumsQuantitiesAndClampsAtZero()
    {
        var procs = new[] { Proc("A") };
        var records = new[] { Rec("A", 3), Rec("A", 2), Rec("A", -7) };

        var card = Assert.Single(WorkloadService.ComputeCards(procs, records,
            new DateTime(2026, 10, 12), WorkloadRecord.DayShift));

        Assert.Equal(0, card.ShiftQty); // 3 + 2 - 7 = -2 → 0
    }

    [Fact]
    public void ComputeCards_LastTimeUsesLatestRecordAndBackfillLabel()
    {
        var procs = new[] { Proc("A") };
        var records = new[]
        {
            Rec("A", 1, hour: 9),
            Rec("A", 1, hour: 11)
        };
        var card = Assert.Single(WorkloadService.ComputeCards(procs, records,
            new DateTime(2026, 10, 12), WorkloadRecord.DayShift));
        Assert.Equal("11:00:00", card.LastTimeText);

        var backfill = Assert.Single(WorkloadService.ComputeCards(procs,
            new[] { Rec("A", 1, hour: 9), Rec("A", 1, hour: 11, backfill: true) },
            new DateTime(2026, 10, 12), WorkloadRecord.DayShift));
        Assert.Equal("补录", backfill.LastTimeText);
    }

    [Fact]
    public void ComputeCards_NoRecords_ShowsPlaceholder()
    {
        var card = Assert.Single(WorkloadService.ComputeCards(new[] { Proc("A") },
            Array.Empty<WorkloadRecord>(), new DateTime(2026, 10, 12), WorkloadRecord.DayShift));

        Assert.Equal(0, card.ShiftQty);
        Assert.Equal("暂无", card.LastTimeText);
    }

    [Theory]
    [InlineData(1, -1, -1)]     // 1 减一不经过 0
    [InlineData(-1, 1, 1)]      // -1 加一不经过 0
    [InlineData(2, -1, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(0, -1, -1)]
    [InlineData(999999, 1, 999999)]
    [InlineData(-999999, -1, -999999)]
    public void StepQuantity_SkipsZeroAndClamps(int current, int delta, int expected)
        => Assert.Equal(expected, WorkloadService.StepQuantity(current, delta));

    [Theory]
    [InlineData(5, -3, -3)]     // 正常冲减
    [InlineData(5, -5, -5)]     // 正好冲减到 0：保留 -5，合计自然归 0
    [InlineData(5, -7, -5)]     // 超量冲减 → 按本班数量
    [InlineData(0, -1, 0)]      // 本班为 0 时无可冲减
    [InlineData(5, 3, 3)]       // 正数原样返回
    public void ClampDeduction_KeepsShiftQuantityAtLeastZero(int shiftQty, int qty, int expected)
        => Assert.Equal(expected, WorkloadService.ClampDeduction(shiftQty, qty));
}
