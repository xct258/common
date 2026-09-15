using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 加密数据仓：内存序列化为 JSON -> AES-256 -> exe 同级目录 .dat 文件。绝不明文存放。
/// （.NET Framework 4.8 内置 DataContractJsonSerializer，无第三方依赖，单 exe 仅几百 KB）
/// </summary>
public static class DataStore
{
    public const string ProjectsFileName = "projects.dat";
    public const string ProcessesFileName = "processes.dat";
    public const string WorkloadFileName = "workload.dat";
    public const string WorkloadProcessFileName = "wprocesses.dat";

    /// <summary>数据文件夹名（exe 同级目录下）。</summary>
    public const string DataFolderName = "Data";

    /// <summary>工作量统计固定项：不存项目字典，排在最后，不可改名删除。</summary>
    public const string FixedMachineProject = "通用";

    public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
    public static string DataDir => Path.Combine(AppDir, DataFolderName);
    public static string ProjectsPath => Path.Combine(DataDir, ProjectsFileName);
    public static string ProcessesPath => Path.Combine(DataDir, ProcessesFileName);
    public static string WorkloadPath => Path.Combine(DataDir, WorkloadFileName);
    public static string WorkloadProcessPath => Path.Combine(DataDir, WorkloadProcessFileName);

    private static string Serialize<T>(T value)
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream();
        ser.WriteObject(ms, value);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static T Deserialize<T>(string json) where T : new()
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        object? obj = ser.ReadObject(ms);
        return obj is T t ? t : new T();
    }

    private static List<T> Load<T>(string path, string password, string badPasswordMsg, string corruptMsg)
    {
        if (!File.Exists(path))
            return new List<T>();
        try
        {
            string json = CryptoService.DecryptFromFile(path, password);
            return Deserialize<List<T>>(json);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(badPasswordMsg, ex);
        }
        catch (SerializationException ex)
        {
            throw new InvalidOperationException(corruptMsg, ex);
        }
    }

    private static void Save<T>(T value, string path, string password)
    {
        Directory.CreateDirectory(DataDir);
        string json = Serialize(value);
        CryptoService.EncryptToFile(json, path, password);
    }

    private static List<string> DistinctSorted(List<string>? items)
    {
        return (items ?? new List<string>())
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }

    // ---- 项目字典 List<string> ----
    public static List<string> LoadProjects(string password)
    {
        return Load<string>(ProjectsPath, password, "口令错误或项目配置文件损坏。", "项目配置文件损坏。");
    }

    public static void SaveProjects(List<string> projects, string password)
    {
        Save(DistinctSorted(projects), ProjectsPath, password);
    }

    // ---- 工序（含逐条操作流程） List<ProcessItem>，加密存 processes.dat ----
    public static List<ProcessItem> LoadProcesses(string password)
    {
        return Load<ProcessItem>(ProcessesPath, password, "口令错误或工序数据文件损坏。", "工序数据文件损坏。");
    }

    public static void SaveProcesses(List<ProcessItem> items, string password)
    {
        Save(items ?? new List<ProcessItem>(), ProcessesPath, password);
    }

    // ---- 工作量记录 List<WorkloadRecord>，加密存 workload.dat ----
    public static List<WorkloadRecord> LoadWorkload(string password)
    {
        return Load<WorkloadRecord>(WorkloadPath, password, "口令错误或工作量数据文件损坏。", "工作量数据文件损坏。");
    }

    public static void SaveWorkload(List<WorkloadRecord> items, string password)
    {
        Save(items ?? new List<WorkloadRecord>(), WorkloadPath, password);
    }

    // ---- 工作量工序 List<WorkloadProcess>，加密存 wprocesses.dat ----
    public static List<WorkloadProcess> LoadWorkloadProcesses(string password)
    {
        return Load<WorkloadProcess>(WorkloadProcessPath, password, "口令错误或工作量工序文件损坏。", "工作量工序文件损坏。");
    }

    public static void SaveWorkloadProcesses(List<WorkloadProcess> items, string password)
    {
        Save(items ?? new List<WorkloadProcess>(), WorkloadProcessPath, password);
    }
}
