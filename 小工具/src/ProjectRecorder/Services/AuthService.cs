using System;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRecorder.Services;

/// <summary>
/// 固定密码验证（不区分大小写，自动去首尾空白）。密码同时是底层 .dat 数据的解密密钥。
/// 统一用 Normalize 后的小写形式做哈希与解密；改密码时对新密码的小写形式取 SHA256 hex 并替换 FixedPasswordHash。
/// 例：python3 -c "import hashlib; print(hashlib.sha256('新密码'.lower().encode()).hexdigest())"
/// 注意：修改密码后旧 .dat 文件无法解密，需删除或迁移数据。
/// </summary>
public static class AuthService
{
    // SHA256("xct258") = faf90bcd...
    private const string FixedPasswordHash = "faf90bcdc162630ac8a5cb225cd641e7ee303c8e0e1fdf3f783784be05b7ac12";

    /// <summary>本会话验证通过的密码（内存中作为解密密钥），退出即丢失。</summary>
    public static string? SessionPassword { get; set; }

    /// <summary>密码规范形式：去首尾空白 + 转小写。登录、校验、解密都用它。</summary>
    public static string Normalize(string? inputPassword)
        => (inputPassword ?? string.Empty).Trim().ToLowerInvariant();

    public static bool Verify(string? inputPassword)
    {
        if (string.IsNullOrEmpty(inputPassword))
            return false;
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(inputPassword)));
        string hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return string.Equals(hex, FixedPasswordHash, StringComparison.OrdinalIgnoreCase);
    }
}
