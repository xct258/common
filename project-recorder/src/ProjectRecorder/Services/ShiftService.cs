using System;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 班次计算：白/夜班划分（8:30 含 ~ 21:00 不含为白班）与班次日期
/// （0:00~8:30 的夜班归前一日）。纯函数，便于单元测试。
/// </summary>
public static class ShiftService
{
    /// <summary>当前班次键：yyyy-MM-dd|白班/夜班，用于检测跨班次刷新。</summary>
    public static string CurrentKey(DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        return $"{WorkloadRecord.GetShiftDate(t):yyyy-MM-dd}|{WorkloadRecord.GetShiftName(t)}";
    }
}
