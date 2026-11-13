using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 主窗口数据访问门面：绑定会话密码，集中错误出口。
/// Load 失败返回 null（错误已通过 Error 事件上报）；Save 返回是否成功。
/// 调用方因此不必重复 try/catch + MessageBox 模板。
/// </summary>
public sealed class DataService
{
    private readonly string _password;

    public DataService(string password)
    {
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    /// <summary>数据读写失败：message 已含“保存失败/删除失败”等前缀，title 用于弹窗标题。</summary>
    public event Action<string, string, Exception>? Error;

    private List<T>? TryLoad<T>(Func<List<T>> load)
    {
        try
        {
            return load();
        }
        catch (InvalidOperationException ex)
        {
            Error?.Invoke(ex.Message, "数据错误", ex);
            return null;
        }
    }

    private bool TrySave(Action save, string failurePrefix)
    {
        try
        {
            save();
            return true;
        }
        catch (Exception ex)
        {
            Error?.Invoke($"{failurePrefix}：{ex.Message}", "错误", ex);
            return false;
        }
    }

    // ---- 项目 ----
    public List<string>? LoadProjects() => TryLoad(() => DataStore.LoadProjects(_password));

    public bool SaveProjects(List<string> projects, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveProjects(projects, _password), failurePrefix);

    /// <summary>
    /// 删除项目及其关联数据（工序与配图 / 工作量 / 工作量工序 / 快捷路径）。
    /// 与原实现一致：任一步失败即中止并上报“删除失败”。
    /// </summary>
    public bool DeleteProject(string project, List<string> projects)
    {
        return TrySave(() =>
        {
            DataStore.SaveProjects(projects, _password);

            var processes = DataStore.LoadProcesses(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveProcesses(processes, _password);
            DataStore.CleanupUnusedImages(processes);

            var workload = DataStore.LoadWorkload(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveWorkload(workload, _password);

            var wprocs = DataStore.LoadWorkloadProcesses(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveWorkloadProcesses(wprocs, _password);

            var shortcuts = DataStore.LoadShortcuts(_password)
                .Where(x => x.ProjectName != project)
                .ToList();
            DataStore.SaveShortcuts(shortcuts, _password);
        }, "删除失败");
    }

    // ---- 工序与图片 ----
    public List<ProcessItem>? LoadProcesses() => TryLoad(() => DataStore.LoadProcesses(_password));

    public bool SaveProcesses(List<ProcessItem> items, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveProcesses(items, _password), failurePrefix);

    public void CleanupUnusedImages(List<ProcessItem> items) => DataStore.CleanupUnusedImages(items);

    public byte[]? GetImageBytes(StepImage image) => DataStore.GetImageBytes(image);

    // ---- 工作量记录 ----
    public List<WorkloadRecord>? LoadWorkload() => TryLoad(() => DataStore.LoadWorkload(_password));

    public bool SaveWorkload(List<WorkloadRecord> items, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveWorkload(items, _password), failurePrefix);

    // ---- 工作量工序 ----
    public List<WorkloadProcess>? LoadWorkloadProcesses() => TryLoad(() => DataStore.LoadWorkloadProcesses(_password));

    public bool SaveWorkloadProcesses(List<WorkloadProcess> items, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveWorkloadProcesses(items, _password), failurePrefix);

    // ---- 快捷路径 ----
    public List<PathShortcut>? LoadShortcuts() => TryLoad(() => DataStore.LoadShortcuts(_password));

    public bool SaveShortcuts(List<PathShortcut> items, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveShortcuts(items, _password), failurePrefix);

    // ---- 笔记 ----
    public List<NoteItem>? LoadNotes() => TryLoad(() => DataStore.LoadNotes(_password));

    public bool SaveNotes(List<NoteItem> items, string failurePrefix = "保存失败")
        => TrySave(() => DataStore.SaveNotes(items, _password), failurePrefix);
}
