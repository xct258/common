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
    public const string ShortcutsFileName = "shortcuts.dat";
    public const string MindMapsFileName = "mindmaps.dat";
    public const string NotesFileName = "notes.dat";
    public const string EncryptedFilesFileName = "files.dat";
    public const string EncryptedFilesFolderName = "files";
    public const string EncryptedFileExtension = ".enc";
    public const string MindGroupsFileName = "mindgroups.dat";
    public const string FileGroupsFileName = "filegroups.dat";

    /// <summary>数据文件夹名（exe 同级目录下）。</summary>
    public const string DataFolderName = "Data";

    /// <summary>步骤配图独立存放的子目录（每图一个加密文件，避免 processes.dat 全量重编码）。</summary>
    public const string ImagesFolderName = "images";

    public const string ImageFileExtension = ".img";

    /// <summary>工作量统计固定项：不存项目字典，排在最后，不可改名删除。</summary>
    public const string FixedMachineProject = "通用";

    /// <summary>思维导图 / 加密文件独立分组的默认分组名（分组与项目列表无关）。</summary>
    public const string DefaultGroup = "默认";

    public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
    public static string DataDir => Path.Combine(AppDir, DataFolderName);
    public static string ProjectsPath => Path.Combine(DataDir, ProjectsFileName);
    public static string ProcessesPath => Path.Combine(DataDir, ProcessesFileName);
    public static string WorkloadPath => Path.Combine(DataDir, WorkloadFileName);
    public static string WorkloadProcessPath => Path.Combine(DataDir, WorkloadProcessFileName);
    public static string ShortcutsPath => Path.Combine(DataDir, ShortcutsFileName);
    public static string MindMapsPath => Path.Combine(DataDir, MindMapsFileName);
    public static string NotesPath => Path.Combine(DataDir, NotesFileName);
    public static string EncryptedFilesPath => Path.Combine(DataDir, EncryptedFilesFileName);
    public static string EncryptedFilesDir => Path.Combine(DataDir, EncryptedFilesFolderName);
    public static string MindGroupsPath => Path.Combine(DataDir, MindGroupsFileName);
    public static string FileGroupsPath => Path.Combine(DataDir, FileGroupsFileName);
    public static string EncryptedFilePath(string id) => Path.Combine(EncryptedFilesDir, id + EncryptedFileExtension);
    public static string ImagesDir => Path.Combine(DataDir, ImagesFolderName);
    public static string ImagePath(string id) => Path.Combine(ImagesDir, id + ImageFileExtension);

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

    // 去重但保持列表顺序（卡片顺序 = 手动排序的结果，保存时不再重排）
    private static List<string> DistinctKeepOrder(List<string>? items)
    {
        return (items ?? new List<string>())
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();
    }

    // ---- 项目字典 List<string> ----
    public static List<string> LoadProjects(string password)
    {
        return Load<string>(ProjectsPath, password, "密码不匹配，无法查看数据。", "项目配置文件损坏。");
    }

    public static void SaveProjects(List<string> projects, string password)
    {
        Save(DistinctKeepOrder(projects), ProjectsPath, password);
    }

    // ---- 步骤配图：每图一个 AES 加密文件（Data/images/<id>.img），processes.dat 只存元数据 ----

    /// <summary>取图片字节：内存里有（新插入/还未保存）直接用，否则从加密文件读。</summary>
    public static byte[]? GetImageBytes(StepImage img)
    {
        if (img.Data != null && img.Data.Length > 0) return img.Data;
        if (string.IsNullOrEmpty(img.Id)) return null;
        string pwd = AuthService.SessionPassword ?? string.Empty;
        if (pwd.Length == 0) return null;
        return LoadImage(img.Id, pwd);
    }

    public static void SaveImage(string id, byte[] data, string password)
    {
        Directory.CreateDirectory(ImagesDir);
        CryptoService.EncryptBytesToFile(data, ImagePath(id), password);
    }

    private static byte[]? LoadImage(string id, string password)
    {
        string path = ImagePath(id);
        if (!File.Exists(path)) return null;
        try
        {
            return CryptoService.DecryptFileToBytes(path, password);
        }
        catch
        {
            return null;
        }
    }

    // 删除 images/ 下不再被任何步骤引用的图片文件（防止删步骤/工序后留下垃圾）
    private static void CleanupOrphanImages(HashSet<string> usedIds)
    {
        try
        {
            if (!Directory.Exists(ImagesDir)) return;
            foreach (string file in Directory.GetFiles(ImagesDir, "*" + ImageFileExtension))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (usedIds.Contains(id)) continue;
                try { File.Delete(file); }
                catch { /* 单个删不掉不影响主流程 */ }
            }
        }
        catch { /* 清理失败不影响保存 */ }
    }

    // ---- 工序（含逐条操作流程） List<ProcessItem>，加密存 processes.dat；配图另存 images/*.img ----
    public static List<ProcessItem> LoadProcesses(string password)
    {
        var list = Load<ProcessItem>(ProcessesPath, password, "密码不匹配，无法查看数据。", "工序数据文件损坏。");
        foreach (var p in list)
        {
            if (p.Steps == null) p.Steps = new List<FlowStep>();
            foreach (var s in p.Steps)
            {
                if (s.Images == null) s.Images = new List<StepImage>();
            }
        }
        return list;
    }

    public static void SaveProcesses(List<ProcessItem> items, string password)
    {
        var list = items ?? new List<ProcessItem>();
        foreach (var img in list
            .Where(p => p.Steps != null)
            .SelectMany(p => p.Steps)
            .Where(s => s.Images != null)
            .SelectMany(s => s.Images))
        {
            if (string.IsNullOrEmpty(img.Id)) img.Id = Guid.NewGuid().ToString();
            if (img.Data != null && img.Data.Length > 0)
            {
                // 新插入图片：写入独立加密文件，processes.dat 里不再内嵌字节
                SaveImage(img.Id, img.Data, password);
                img.Data = null;
            }
        }
        Save(list, ProcessesPath, password);
    }

    /// <summary>删除 images/ 下已不再被引用的图片文件（删除步骤/工序/项目后调用）。</summary>
    public static void CleanupUnusedImages(List<ProcessItem> items)
    {
        var used = new HashSet<string>((items ?? new List<ProcessItem>())
            .Where(p => p.Steps != null)
            .SelectMany(p => p.Steps)
            .Where(s => s.Images != null)
            .SelectMany(s => s.Images)
            .Select(x => x.Id)
            .Where(id => !string.IsNullOrEmpty(id)));
        CleanupOrphanImages(used);
    }

    // ---- 工作量记录 List<WorkloadRecord>，加密存 workload.dat ----
    public static List<WorkloadRecord> LoadWorkload(string password)
    {
        return Load<WorkloadRecord>(WorkloadPath, password, "密码不匹配，无法查看数据。", "工作量数据文件损坏。");
    }

    public static void SaveWorkload(List<WorkloadRecord> items, string password)
    {
        Save(items ?? new List<WorkloadRecord>(), WorkloadPath, password);
    }

    // ---- 工作量工序 List<WorkloadProcess>，加密存 wprocesses.dat ----
    public static List<WorkloadProcess> LoadWorkloadProcesses(string password)
    {
        return Load<WorkloadProcess>(WorkloadProcessPath, password, "密码不匹配，无法查看数据。", "工作量工序文件损坏。");
    }

    public static void SaveWorkloadProcesses(List<WorkloadProcess> items, string password)
    {
        Save(items ?? new List<WorkloadProcess>(), WorkloadProcessPath, password);
    }

    // ---- 快捷路径 List<PathShortcut>，加密存 shortcuts.dat ----
    public static List<PathShortcut> LoadShortcuts(string password)
    {
        return Load<PathShortcut>(ShortcutsPath, password, "密码不匹配，无法查看数据。", "快捷路径文件损坏。");
    }

    public static void SaveShortcuts(List<PathShortcut> items, string password)
    {
        Save(items ?? new List<PathShortcut>(), ShortcutsPath, password);
    }

    // ---- 思维导图 List<MindMap>，加密存 mindmaps.dat ----

    public static List<MindMap> LoadMindMaps(string password)
    {
        var list = Load<MindMap>(MindMapsPath, password, "密码不匹配，无法查看数据。", "思维导图数据文件损坏。");
        foreach (var map in list)
        {
            map.Root ??= new MindNode();
            if (string.IsNullOrWhiteSpace(map.ProjectName)) map.ProjectName = DefaultGroup;
            NormalizeMindNode(map.Root);
        }
        return list;
    }

    private static void NormalizeMindNode(MindNode node)
    {
        if (string.IsNullOrEmpty(node.Id)) node.Id = Guid.NewGuid().ToString();
        // 旧版节点只有 Text（纯文本），迁移为标题
        string? legacy = node.LegacyText;
        if (string.IsNullOrEmpty(node.Title) && !string.IsNullOrEmpty(legacy))
            node.Title = legacy!;
        node.LegacyText = null;
        node.Title ??= string.Empty;
        node.Content ??= string.Empty;
        node.Children ??= new List<MindNode>();
        foreach (var child in node.Children)
            NormalizeMindNode(child);
    }

    public static void SaveMindMaps(List<MindMap> items, string password)
    {
        Save(items ?? new List<MindMap>(), MindMapsPath, password);
    }

    // ---- 笔记 List<NoteItem>，加密存 notes.dat ----

    public static List<NoteItem> LoadNotes(string password)
    {
        var list = Load<NoteItem>(NotesPath, password, "密码不匹配，无法查看数据。", "笔记数据文件损坏。");
        foreach (var note in list)
        {
            if (string.IsNullOrEmpty(note.Id)) note.Id = Guid.NewGuid().ToString();
            note.Title ??= string.Empty;
            note.Content ??= string.Empty;
        }
        return list;
    }

    public static void SaveNotes(List<NoteItem> items, string password)
    {
        Save(items ?? new List<NoteItem>(), NotesPath, password);
    }

    // ---- 加密文件（元数据 List<EncryptedFile> 加密存 files.dat；内容每文件一个 AES 文件） ----

    public static List<EncryptedFile> LoadEncryptedFiles(string password)
    {
        var list = Load<EncryptedFile>(EncryptedFilesPath, password, "密码不匹配，无法查看数据。", "加密文件数据损坏。");
        foreach (var item in list)
        {
            if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
            if (string.IsNullOrWhiteSpace(item.ProjectName)) item.ProjectName = DefaultGroup;
            item.Name ??= string.Empty;
            item.OriginalName ??= string.Empty;
            if (item.Size < 0) item.Size = 0;
            if (item.UpdatedTime == default) item.UpdatedTime = item.CreatedTime;
        }
        return list;
    }

    public static void SaveEncryptedFiles(List<EncryptedFile> items, string password)
    {
        Save(items ?? new List<EncryptedFile>(), EncryptedFilesPath, password);
    }

    /// <summary>写入一个加密文件内容（Data/files/&lt;id&gt;.enc）。</summary>
    public static void SaveEncryptedFileContent(string id, byte[] data, string password)
    {
        Directory.CreateDirectory(EncryptedFilesDir);
        CryptoService.EncryptBytesToFile(data, EncryptedFilePath(id), password);
    }

    /// <summary>读取并解密一个文件内容（返回明文字节）。</summary>
    public static byte[] LoadEncryptedFileContent(string id, string password)
    {
        return CryptoService.DecryptFileToBytes(EncryptedFilePath(id), password);
    }

    /// <summary>删除一个文件内容（删记录时调用，失败不影响主流程）。</summary>
    public static void DeleteEncryptedFileContent(string id)
    {
        try
        {
            string path = EncryptedFilePath(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 单个文件删不掉不影响主流程
        }
    }

    // ---- 独立分组 List<string>（思维导图 / 加密文件各一套，与项目列表无关） ----

    private static List<string> LoadGroups(string path, string password, string corruptMsg)
    {
        var list = Load<string>(path, password, "密码不匹配，无法查看数据。", corruptMsg)
            .Select(g => (g ?? string.Empty).Trim())
            .Where(g => g.Length > 0)
            .Distinct()
            .ToList();
        if (list.Count == 0) list.Add(DefaultGroup);
        return list;
    }

    public static List<string> LoadMindGroups(string password)
        => LoadGroups(MindGroupsPath, password, "思维导图分组配置损坏。");

    public static void SaveMindGroups(List<string> groups, string password)
        => Save(NormalizeGroups(groups), MindGroupsPath, password);

    public static List<string> LoadFileGroups(string password)
        => LoadGroups(FileGroupsPath, password, "加密文件分组配置损坏。");

    public static void SaveFileGroups(List<string> groups, string password)
        => Save(NormalizeGroups(groups), FileGroupsPath, password);

    private static List<string> NormalizeGroups(List<string>? groups)
    {
        var list = (groups ?? new List<string>())
            .Select(g => (g ?? string.Empty).Trim())
            .Where(g => g.Length > 0)
            .Distinct()
            .ToList();
        if (list.Count == 0) list.Add(DefaultGroup);
        return list;
    }

}
