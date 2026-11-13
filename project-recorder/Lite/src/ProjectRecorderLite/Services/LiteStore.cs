using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using ProjectRecorderLite.Models;

namespace ProjectRecorderLite.Services;

/// <summary>Lite 版的全部数据：项目列表 + 快捷路径。</summary>
[DataContract]
public sealed class LiteData
{
    [DataMember] public List<string> Projects { get; set; } = new();
    [DataMember] public List<PathShortcut> Shortcuts { get; set; } = new();
}

/// <summary>
/// Lite 版明文 JSON 数据存储（无登录、无加密）。
/// 数据直接追加在 exe 末尾，保存后由后台脚本在程序退出时写回 exe。
/// 增删改在内存中完成并立即落盘，失败时回滚内存改动。
/// </summary>
public static class LiteStore
{
    /// <summary>固定「通用」模块：不属于项目列表，网址快捷路径只能放在这里。</summary>
    public const string GeneralProject = "通用";

    private static LiteData? _data;

    /// <summary>最近一次保存失败的原因；成功后清空。</summary>
    public static string? LastError { get; private set; }

    /// <summary>启动时数据读取提示（只用于第一次加载）。</summary>
    public static string? StartupWarning { get; private set; }

    private static LiteData Data => _data ??= LoadFromDisk();

    /// <summary>项目列表（内部真实引用，按显示顺序）。</summary>
    public static List<string> Projects => Data.Projects;

    public static List<PathShortcut> GetShortcuts(string project)
    {
        if (string.IsNullOrWhiteSpace(project)) return new List<PathShortcut>();
        return Data.Shortcuts
            .Where(x => string.Equals(x.ProjectName, project, StringComparison.Ordinal))
            .ToList();
    }

    // ---------- 项目 ----------

    public static bool AddProject(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return false;
        if (string.Equals(name, GeneralProject, StringComparison.Ordinal)) return false;
        if (Data.Projects.Contains(name)) return true;

        Data.Projects.Add(name);
        if (TrySave()) return true;
        Data.Projects.Remove(name);
        return false;
    }

    public static bool RemoveProject(string name)
    {
        if (string.Equals(name, GeneralProject, StringComparison.Ordinal)) return false;

        int index = Data.Projects.IndexOf(name);
        if (index < 0) return false;

        var removedShortcuts = Data.Shortcuts
            .Where(x => string.Equals(x.ProjectName, name, StringComparison.Ordinal))
            .ToList();

        Data.Projects.RemoveAt(index);
        Data.Shortcuts.RemoveAll(x => string.Equals(x.ProjectName, name, StringComparison.Ordinal));

        if (TrySave()) return true;

        // 回滚
        Data.Projects.Insert(Math.Min(index, Data.Projects.Count), name);
        Data.Shortcuts.AddRange(removedShortcuts);
        return false;
    }

    public static bool MoveProject(string name, int delta)
    {
        if (string.Equals(name, GeneralProject, StringComparison.Ordinal)) return true;

        int i = Data.Projects.IndexOf(name);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Data.Projects.Count) return true;

        (Data.Projects[i], Data.Projects[j]) = (Data.Projects[j], Data.Projects[i]);
        if (TrySave()) return true;
        (Data.Projects[i], Data.Projects[j]) = (Data.Projects[j], Data.Projects[i]);
        return false;
    }

    // ---------- 快捷路径 ----------

    public static bool AddShortcut(PathShortcut shortcut)
    {
        if (shortcut == null) return false;

        shortcut.ProjectName = (shortcut.ProjectName ?? string.Empty).Trim();
        // 网址快捷路径只能放在「通用」
        if (shortcut.IsUrl
            && !string.Equals(shortcut.ProjectName, GeneralProject, StringComparison.Ordinal))
            return false;

        shortcut.Id = string.IsNullOrEmpty(shortcut.Id) ? Guid.NewGuid().ToString() : shortcut.Id;
        shortcut.CreatedTime = shortcut.CreatedTime == default ? DateTime.Now : shortcut.CreatedTime;

        Data.Shortcuts.Add(shortcut);
        if (TrySave()) return true;
        Data.Shortcuts.Remove(shortcut);
        return false;
    }

    public static bool RemoveShortcut(string id)
    {
        int index = Data.Shortcuts.FindIndex(x => x.Id == id);
        if (index < 0) return false;

        var removed = Data.Shortcuts[index];
        Data.Shortcuts.RemoveAt(index);
        if (TrySave()) return true;
        Data.Shortcuts.Insert(Math.Min(index, Data.Shortcuts.Count), removed);
        return false;
    }

    // ---------- 落盘 ----------

    private static bool TrySave()
    {
        try
        {
            SaveCore();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static void SaveCore()
    {
        var serializer = new DataContractJsonSerializer(typeof(LiteData));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, Data);
        byte[] json = ms.ToArray();

        if (!ExeDataStore.SavePayload(json, out string error))
            throw new InvalidOperationException(error);
    }

    private static LiteData LoadFromDisk()
    {
        byte[]? json = ExeDataStore.LoadPayload(out string? warning);
        if (!string.IsNullOrEmpty(warning))
            StartupWarning = warning;

        if (json == null || json.Length == 0)
            return new LiteData();

        try
        {
            var serializer = new DataContractJsonSerializer(typeof(LiteData));
            using var ms = new MemoryStream(json);
            var data = serializer.ReadObject(ms) as LiteData ?? new LiteData();
            Normalize(data);

            // 旧版 Data/data.json：读入后立即生成 pending，退出时自动写进 exe
            if (ExeDataStore.UsedLegacyFile)
                TryMigrateLegacyToExe(data);

            // 如果本次是从 .pending 读到的，App.OnExit 会在退出时把它写回 exe
            return data;
        }
        catch (Exception ex)
        {
            // 损坏时先备份，避免用户数据被无声覆盖
            try
            {
                string backup = ExeDataStore.ExePath
                                + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}.json";
                File.WriteAllBytes(backup, json);
            }
            catch
            {
                // 备份失败不阻断启动
            }

            // 损坏的 pending 不能再覆盖 exe
            if (ExeDataStore.LoadedFromPending)
                ExeDataStore.DiscardPending();

            StartupWarning = "exe 内的数据读取失败，已按空数据启动（原数据已尝试备份）。\n" + ex.Message;
            return new LiteData();
        }
    }

    private static void TryMigrateLegacyToExe(LiteData data)
    {
        try
        {
            var serializer = new DataContractJsonSerializer(typeof(LiteData));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, data);

            if (!ExeDataStore.SavePayload(ms.ToArray(), out string error))
                StartupWarning = (StartupWarning ?? string.Empty) + "\n旧数据写入 exe 失败：" + error;
        }
        catch (Exception ex)
        {
            StartupWarning = (StartupWarning ?? string.Empty) + "\n旧数据写入 exe 失败：" + ex.Message;
        }
    }

    private static void Normalize(LiteData data)
    {
        data.Projects ??= new List<string>();
        data.Shortcuts ??= new List<PathShortcut>();

        data.Projects = data.Projects
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Where(p => !string.Equals(p, GeneralProject, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        // 允许的归属：固定「通用」+ 全部项目
        var ownerSet = new HashSet<string>(data.Projects, StringComparer.Ordinal) { GeneralProject };

        var shortcuts = new List<PathShortcut>();
        foreach (var sc in data.Shortcuts)
        {
            if (sc == null) continue;

            sc.Id = string.IsNullOrEmpty(sc.Id) ? Guid.NewGuid().ToString() : sc.Id;
            sc.ProjectName = (sc.ProjectName ?? string.Empty).Trim();
            sc.Kind = string.IsNullOrEmpty(sc.Kind) ? PathShortcut.KindFolder : sc.Kind;
            sc.Name = sc.Name ?? string.Empty;
            sc.FolderPath = sc.FolderPath ?? string.Empty;
            if (sc.CreatedTime == default) sc.CreatedTime = DateTime.Now;

            // 网址只能放在「通用」：历史数据里其它项目下的网址迁移到通用，避免丢失
            if (sc.IsUrl && !string.Equals(sc.ProjectName, GeneralProject, StringComparison.Ordinal))
                sc.ProjectName = GeneralProject;

            // 丢弃找不到归属（通用或已存在项目）的脏数据
            if (!ownerSet.Contains(sc.ProjectName)) continue;
            shortcuts.Add(sc);
        }
        data.Shortcuts = shortcuts;
    }
}
