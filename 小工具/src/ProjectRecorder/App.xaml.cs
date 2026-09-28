using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ProjectRecorder.Services;

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

            // AvalonEdit 程序集内嵌在 exe 里（保持单文件发布），首次用到时从资源加载
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssemblies;

            // 记住了登录：跳过登录框直接进入（配置文件损坏/换 Windows 用户则回退到登录）
            if (AutoLoginStore.TryLoad(out string savedPassword))
            {
                AuthService.SessionPassword = savedPassword;
            }
            else
            {
                AutoLoginStore.Disable();
                var login = new LoginWindow();
                bool? ok = login.ShowDialog();
                if (ok != true)
                {
                    Shutdown();
                    return;
                }
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

    private static readonly object _embeddedLock = new();
    private static Dictionary<string, byte[]>? _embeddedDllBytes;
    private static readonly Dictionary<string, Assembly?> _embeddedLoaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>从内嵌 zip 按需加载第三方程序集（单文件发布用），同名只加载一次保证类型标识一致。
    /// zip 由构建时的 PackEmbeddedLibs 目标从实际依赖闭包生成，升级包后无需手工维护。</summary>
    private static Assembly? ResolveEmbeddedAssemblies(object? sender, ResolveEventArgs args)
    {
        string? name;
        try
        {
            name = new AssemblyName(args.Name).Name;
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(name) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            return null;
        lock (_embeddedLock)
        {
            if (_embeddedLoaded.TryGetValue(name, out Assembly? cached)) return cached;
            Assembly? loaded = null;
            try
            {
                _embeddedDllBytes ??= ReadEmbeddedDlls();
                if (_embeddedDllBytes.TryGetValue(name + ".dll", out byte[]? bytes) && bytes != null)
                    loaded = Assembly.Load(bytes);
            }
            catch
            {
                loaded = null;
            }
            _embeddedLoaded[name] = loaded;
            return loaded;
        }
    }

    private static Dictionary<string, byte[]> ReadEmbeddedDlls()
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("ProjectRecorder.Resources.libs.zip");
            if (stream == null) return map;
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) continue;
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                map[entry.Name] = buffer.ToArray();
            }
        }
        catch
        {
            // 内嵌库读失败：调用方走正常探测，找不到再报缺失
        }
        return map;
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
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 无 UI 环境下忽略
        }
    }
}
