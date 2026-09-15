using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ProjectRecorder;

/// <summary>
/// 先弹登录窗，验证通过才进工作台。
/// 附带全局异常捕获：任何启动期崩溃都会弹窗 + 写 exe 同级 error.log，避免“双击没反应”无法定位。
/// </summary>
public partial class App : Application
{
    private static string LogPath
    {
        get
        {
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "ProjectRecorder_error.log");
            }
        }
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
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

            var login = new LoginWindow();
            bool? ok = login.ShowDialog();
            if (ok != true)
            {
                Shutdown();
                return;
            }

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
            MessageBox.Show($"程序出现错误（{where}）：\n{ex?.Message}\n\n详细信息已写入 error.log（exe 同级目录）。",
                "小工具 - 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 无 UI 环境下忽略
        }
    }
}
