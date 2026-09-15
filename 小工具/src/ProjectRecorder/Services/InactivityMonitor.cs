using System;
using System.Windows.Threading;

namespace ProjectRecorder.Services;

/// <summary>
/// 超时防窥（软件内）：只统计本软件窗口内的鼠标+键盘操作。
/// 在软件外动鼠标/键盘不会重置计时；软件内连续 30 秒无交互即触发 TimedOut，调用方强制退出。
/// 计时重置靠窗口的 Preview* 事件调用 NotifyActivity()。
/// </summary>
public sealed class InactivityMonitor : IDisposable
{
    public const int TimeoutSeconds = 30;

    public event Action? TimedOut;
    public event Action<int>? Tick;

    private readonly DispatcherTimer _timer;
    private DateTime _lastActivity = DateTime.Now;
    private bool _disposed;

    public InactivityMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _lastActivity = DateTime.Now;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>软件内有任何鼠标/键盘活动时调用，重置 30 秒计时。</summary>
    public void NotifyActivity() => _lastActivity = DateTime.Now;

    private void OnTick(object? sender, EventArgs e)
    {
        double idle = (DateTime.Now - _lastActivity).TotalSeconds;
        int remain = TimeoutSeconds - (int)idle;
        if (remain < 0) remain = 0;
        Tick?.Invoke(remain);
        if (idle >= TimeoutSeconds)
        {
            Stop();
            TimedOut?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
