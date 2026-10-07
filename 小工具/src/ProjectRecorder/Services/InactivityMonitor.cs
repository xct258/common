using System;
using System.Threading;
using System.Windows.Threading;

namespace ProjectRecorder.Services;

/// <summary>
/// 超时防窥（软件内）：只统计本软件窗口内的鼠标+键盘操作。
/// 在软件外动鼠标/键盘不会重置计时；软件内连续 120 秒无交互即触发 TimedOut，调用方强制退出。
/// 计时重置靠窗口的 Preview* 事件调用 NotifyActivity()。
/// 另有一个后台看门狗：原生模态框（如 MessageBox）会阻塞 DispatcherTimer，
/// 此时仍由看门狗在超时后强制退出，保证「错误弹窗」也能自动退出（HardExitEnabled 关闭时跳过）。
/// </summary>
public sealed class InactivityMonitor : IDisposable
{
    public const int TimeoutSeconds = 120;

    public event Action? TimedOut;
    public event Action<int>? Tick;

    /// <summary>超时后是否强制退出进程（默认为 true）。记住登录时由主窗口置为 false。</summary>
    public bool HardExitEnabled { get; set; } = true;

    private readonly DispatcherTimer _timer;
    private readonly System.Threading.Timer _watchdog;
    private long _lastActivityTicks = DateTime.Now.Ticks;
    private bool _disposed;

    public InactivityMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTick;
        _watchdog = new System.Threading.Timer(WatchdogTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        NotifyActivity();
        _timer.Start();
        _watchdog.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer.Stop();
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>软件内有任何鼠标/键盘活动时调用，重置 120 秒计时。</summary>
    public void NotifyActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.Now.Ticks);

    private int IdleSeconds()
    {
        long last = Interlocked.Read(ref _lastActivityTicks);
        return (int)(DateTime.Now - new DateTime(last)).TotalSeconds;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        int idle = IdleSeconds();
        int remain = TimeoutSeconds - idle;
        if (remain < 0) remain = 0;
        Tick?.Invoke(remain);
        if (idle >= TimeoutSeconds)
        {
            Stop();
            TimedOut?.Invoke();
        }
    }

    // 看门狗：原生模态框打开时 DispatcherTimer 可能不触发，这里在后台线程兜底强制退出
    private void WatchdogTick(object? state)
    {
        if (_disposed || !HardExitEnabled) return;
        if (IdleSeconds() >= TimeoutSeconds)
            Environment.Exit(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _watchdog.Dispose();
    }
}
