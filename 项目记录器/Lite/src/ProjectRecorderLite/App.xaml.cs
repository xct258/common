using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ProjectRecorderLite.Services;

namespace ProjectRecorderLite;

/// <summary>
/// Lite 版启动：无登录、无密码，直接进入主窗口。
/// 保留单实例与全局异常捕获，保证双击没反应时能写日志定位。
/// </summary>
public partial class App : Application
{
    // 日志写到临时目录，避免在 exe 旁边留下文件（Lite 版数据在 exe 内部，保持单文件）
    private static string LogPath
    {
        get
        {
            try
            {
                return Path.Combine(Path.GetTempPath(), "小工具Lite-error.log");
            }
            catch
            {
                return "小工具Lite-error.log";
            }
        }
    }

    /// <summary>单实例互斥锁：避免重复启动多个 exe 同时读写同一份 JSON 数据。</summary>
    private static Mutex? _singleInstanceMutex;
    private const string SingleInstanceName = @"Local\ProjectRecorderLite.SingleInstance";
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static bool TryLockSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceName, out bool createdNew);
            if (createdNew) return true;
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                using (p)
                {
                    if (p.Id == current.Id) continue;
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    break;
                }
            }
        }
        catch
        {
            // 置前失败不影响退出
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 退出前启动替换脚本：等本进程结束后把 .pending 覆盖回 exe
        try { ExeDataStore.EnsureSwapHelper(); } catch { /* 写回失败不阻断退出 */ }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // 未持有/已释放时忽略
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (!TryLockSingleInstance())
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
                ReportFatal("AppDomain", ev.ExceptionObject as Exception);
            DispatcherUnhandledException += (_, ev) =>
            {
                ReportFatal("UI", ev.Exception);
                ev.Handled = true;
                Shutdown();
            };
            TaskScheduler.UnobservedTaskException += (_, ev) =>
            {
                ReportFatal("Task", ev.Exception);
                ev.SetObserved();
            };

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            ReportFatal("Startup", ex);
            Shutdown();
        }
    }

    internal static void ReportFatal(string where, Exception? ex)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\r\n\r\n",
                Encoding.UTF8);
        }
        catch
        {
            // 日志写失败也不要再抛异常
        }

        try
        {
            MessageBox.Show($"程序出现错误（{where}）：\n{ex?.Message}\n\n详细信息已写入：\n{LogPath}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 无 UI 环境下忽略
        }
    }
}
