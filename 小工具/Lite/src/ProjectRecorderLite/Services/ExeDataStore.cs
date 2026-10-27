using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace ProjectRecorderLite.Services;

/// <summary>
/// 单文件数据存储：把明文 JSON 直接追加在 exe 末尾。
///
/// Windows 下运行中的 exe 被系统锁定，不能直接改写自身，所以保存流程是：
/// 1. 生成一份「当前 exe + 新数据」的完整副本，写到 &lt;exe&gt;.pending；
/// 2. 程序正常退出前，启动一个后台 PowerShell 等待当前进程结束；
/// 3. 进程结束后，用 pending 覆盖原 exe。
///
/// 正常退出后目录里只剩一个 exe；异常结束时会留下 .pending，
/// 下次启动会自动从 .pending 读取，并在退出时再次尝试写回。
/// </summary>
public static class ExeDataStore
{
    // 标记中带 NUL 字节，正常 JSON 文本不会出现这些字节，扫描时不会误判。
    private static readonly byte[] StartMarker =
        Encoding.UTF8.GetBytes("\0XCT_PROJECT_RECORDER_LITE_DATA_V1_START_9F3A7C\0");

    private static readonly byte[] EndMarker =
        Encoding.UTF8.GetBytes("\0XCT_PROJECT_RECORDER_LITE_DATA_V1_END_9F3A7C\0");

    private const string PendingSuffix = ".pending";
    private const string LegacyFolder = "Data";
    private const string LegacyFile = "data.json";

    private static string? _exePath;
    private static bool _swapHelperStarted;

    public static string ExePath => _exePath ??= ResolveExePath();
    public static string ExeDir => Path.GetDirectoryName(ExePath) ?? AppDomain.CurrentDomain.BaseDirectory;
    public static string PendingPath => ExePath + PendingSuffix;
    public static bool PendingExists => File.Exists(PendingPath);

    /// <summary>本次数据是否来自旧版 Data/data.json。</summary>
    public static bool UsedLegacyFile { get; private set; }

    /// <summary>本次数据是否来自尚未写回 exe 的 .pending。</summary>
    public static bool LoadedFromPending { get; private set; }

    /// <summary>
    /// 读取数据：优先 .pending（比 exe 新），其次 exe 末尾，最后兼容旧版 Data/data.json。
    /// 没有数据时返回 null。
    /// </summary>
    public static byte[]? LoadPayload(out string? warning)
    {
        warning = null;
        UsedLegacyFile = false;
        LoadedFromPending = false;

        // 1) 上次保存还没写回 exe 的数据
        if (File.Exists(PendingPath))
        {
            if (TryReadPayloadFile(PendingPath, out byte[]? pendingJson, out string? pendingError))
            {
                LoadedFromPending = true;
                warning = "上次保存的数据尚未写回 exe，已从 .pending 读取；退出后会再次尝试写回。";
                return pendingJson;
            }

            // 损坏的 pending 不能覆盖 exe，先改名保留现场
            string bad = PendingPath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Move(PendingPath, bad); } catch { /* 改名失败也不阻断 */ }

            warning = "发现 .pending 但内容无法读取，已忽略（已尝试改名为 "
                      + Path.GetFileName(bad) + "）。" + pendingError;
        }

        // 2) exe 自身末尾的数据
        if (TryReadPayloadFile(ExePath, out byte[]? exeJson, out string? exeError))
            return exeJson;

        // 3) 兼容旧版：Data/data.json 只读一次
        string legacy = Path.Combine(ExeDir, LegacyFolder, LegacyFile);
        if (File.Exists(legacy))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(legacy);
                UsedLegacyFile = true;
                warning = "已读取旧版 Data/data.json；退出后会写入 exe，旧文件可自行删除。";
                return bytes;
            }
            catch (Exception ex)
            {
                warning = "旧版 Data/data.json 读取失败：" + ex.Message;
            }
        }

        if (!string.IsNullOrEmpty(exeError))
            warning = exeError;

        return null;
    }

    /// <summary>程序退出前调用：如果存在 .pending，启动后台替换脚本。</summary>
    public static void EnsureSwapHelper()
    {
        if (File.Exists(PendingPath))
            StartSwapHelper();
    }

    /// <summary>把新数据写进 pending；真正的 exe 替换在进程退出时完成。</summary>
    public static bool SavePayload(byte[] json, out string error)
    {
        error = string.Empty;
        string pending = PendingPath;
        string temp = pending + ".tmp";

        try
        {
            // 以当前 exe 为基底（去掉旧数据），再追加新数据
            BuildExeWithPayload(ExePath, json, temp);

            if (File.Exists(pending))
            {
                try
                {
                    File.Replace(temp, pending, null);
                }
                catch
                {
                    File.Delete(pending);
                    File.Move(temp, pending);
                }
            }
            else
            {
                File.Move(temp, pending);
            }

            // 替换在 App.OnExit 里启动，避免整个使用期间都挂着一个后台进程
            return true;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 忽略清理失败 */ }
            error = ex.Message;
            return false;
        }
    }

    /// <summary>丢弃无法解析的 pending，避免它退出后覆盖 exe。</summary>
    public static void DiscardPending()
    {
        try
        {
            if (File.Exists(PendingPath))
                File.Move(PendingPath, PendingPath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        }
        catch
        {
            // 失败时保留原文件，至少不丢数据
        }
    }

    // ---------- 内部实现（测试可直接调用） ----------

    /// <summary>仅测试用：把数据目标指向临时文件，并禁用后台替换脚本。</summary>
    internal static void UseTestExePath(string path)
    {
        _exePath = Path.GetFullPath(path);
        _swapHelperStarted = true;
    }

    internal static int StartMarkerLength => StartMarker.Length;
    internal static int EndMarkerLength => EndMarker.Length;

    internal static bool TryReadPayloadFile(string path, out byte[]? json, out string? error)
    {
        json = null;
        error = null;

        try
        {
            byte[] all = File.ReadAllBytes(path);
            int start = LastIndexOf(all, StartMarker);
            if (start < 0) return false; // 正常：exe 里还没有数据

            int payloadStart = start + StartMarker.Length;
            int end = LastIndexOf(all, EndMarker, payloadStart);
            if (end < payloadStart)
            {
                error = "数据缺少结束标记，文件可能被破坏。";
                return false;
            }

            json = new byte[end - payloadStart];
            Buffer.BlockCopy(all, payloadStart, json, 0, json.Length);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void BuildExeWithPayload(string sourceExe, byte[] json, string dest)
    {
        byte[] all = File.ReadAllBytes(sourceExe);

        // 如果 exe 已有旧数据，从旧数据的起始标记处截断，保留原始 exe 部分
        int oldStart = LastIndexOf(all, StartMarker);
        int baseLength = oldStart >= 0 ? oldStart : all.Length;

        using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(all, 0, baseLength);
        fs.Write(StartMarker, 0, StartMarker.Length);
        fs.Write(json, 0, json.Length);
        fs.Write(EndMarker, 0, EndMarker.Length);
        fs.Flush(true);
    }

    internal static int LastIndexOf(byte[] haystack, byte[] needle, int minIndex = 0)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        if (minIndex < 0) minIndex = 0;

        for (int i = haystack.Length - needle.Length; i >= minIndex; i--)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match) return i;
        }

        return -1;
    }

    private static void StartSwapHelper()
    {
        if (_swapHelperStarted) return;

        try
        {
            int pid = Process.GetCurrentProcess().Id;

            // 路径用单引号包裹；单引号本身在 PowerShell 里用两个单引号转义
            string pending = PsQuote(PendingPath);
            string exe = PsQuote(ExePath);

            string command =
                $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; " +
                "Start-Sleep -Milliseconds 500; " +
                $"Move-Item -LiteralPath '{pending}' -Destination '{exe}' -Force -ErrorAction SilentlyContinue";

            string psExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(psExe)) psExe = "powershell.exe";

            var psi = new ProcessStartInfo
            {
                FileName = psExe,
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"" + command + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            _swapHelperStarted = true;
        }
        catch
        {
            // 启动失败不阻断保存：pending 还在，下次启动会继续尝试
        }
    }

    private static string PsQuote(string value) => value.Replace("'", "''");

    private static string ResolveExePath()
    {
        try
        {
            var module = Process.GetCurrentProcess().MainModule;
            if (module != null && !string.IsNullOrEmpty(module.FileName))
                return Path.GetFullPath(module.FileName);
        }
        catch
        {
            // 继续尝试其他方式
        }

        try
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry != null && !string.IsNullOrEmpty(entry.Location))
                return Path.GetFullPath(entry.Location);
        }
        catch
        {
            // 继续尝试其他方式
        }

        return Path.GetFullPath(Process.GetCurrentProcess().ProcessName + ".exe");
    }
}
