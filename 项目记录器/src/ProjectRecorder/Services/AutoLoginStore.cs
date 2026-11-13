using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRecorder.Services;

/// <summary>
/// 记住登录：在用户文件夹 `%APPDATA%\ProjectRecorder\auto-login.dat` 保存登录信息，
/// 内容用 Windows DPAPI（仅当前 Windows 用户可解密）加密，绝不存明文。
/// 该文件存在 = 下次启动自动登录，同时取消 120 秒无操作自动退出。
/// </summary>
public static class AutoLoginStore
{
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProjectRecorder");

    public static string ConfigPath => Path.Combine(ConfigDir, "auto-login.dat");

    public static bool IsEnabled => File.Exists(ConfigPath);

    /// <summary>尝试读取并解密保存的登录信息；成功返回 true 并输出规范密码。</summary>
    public static bool TryLoad(out string password)
    {
        password = string.Empty;
        try
        {
            if (!File.Exists(ConfigPath)) return false;

            byte[] plain = ProtectedData.Unprotect(
                File.ReadAllBytes(ConfigPath), null, DataProtectionScope.CurrentUser);
            string saved = AuthService.Normalize(Encoding.UTF8.GetString(plain));
            if (saved.Length == 0) return false;
            if (!AuthService.Verify(saved)) return false; // 密码已变更或数据异常

            password = saved;
            return true;
        }
        catch
        {
            // 换 Windows 用户/文件损坏等：按未保存处理
            return false;
        }
    }

    /// <summary>保存登录信息（DPAPI 当前用户加密）。</summary>
    public static void Enable(string password)
    {
        Directory.CreateDirectory(ConfigDir);
        byte[] cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(AuthService.Normalize(password)), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ConfigPath, cipher);
    }

    /// <summary>删除保存的登录信息（取消记住登录）。</summary>
    public static void Disable()
    {
        try
        {
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
        }
        catch
        {
            // 删除失败不影响本次运行
        }
    }
}
