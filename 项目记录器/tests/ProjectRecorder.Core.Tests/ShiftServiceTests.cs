using ProjectRecorder.Models;
using ProjectRecorder.Services;
using Xunit;

namespace ProjectRecorder.Core.Tests;

/// <summary>班次规则：8:30（含）~21:00（不含）白班，其余夜班；0:00~8:30 归前一日夜班。</summary>
public class ShiftServiceTests
{
    [Theory]
    [InlineData("2026-10-12 00:00:00", "2026-10-11", "夜班")]
    [InlineData("2026-10-12 08:29:59", "2026-10-11", "夜班")]
    [InlineData("2026-10-12 08:30:00", "2026-10-12", "白班")]
    [InlineData("2026-10-12 20:59:59", "2026-10-12", "白班")]
    [InlineData("2026-10-12 21:00:00", "2026-10-12", "夜班")]
    [InlineData("2026-10-12 23:59:59", "2026-10-12", "夜班")]
    public void ShiftDateAndName_FollowBoundaryRules(string now, string expectedDate, string expectedShift)
    {
        DateTime t = DateTime.Parse(now);

        Assert.Equal(DateTime.Parse(expectedDate), WorkloadRecord.GetShiftDate(t));
        Assert.Equal(expectedShift, WorkloadRecord.GetShiftName(t));
    }

    [Fact]
    public void CurrentKey_CombinesDateAndShift()
    {
        Assert.Equal("2026-10-11|夜班", ShiftService.CurrentKey(new DateTime(2026, 10, 12, 7, 0, 0)));
        Assert.Equal("2026-10-12|白班", ShiftService.CurrentKey(new DateTime(2026, 10, 12, 9, 0, 0)));
    }
}
