using System;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRecorder.Services;

/// <summary>
/// 固定口令验证。口令同时是底层 .dat 数据的解密密钥。
/// 修改口令方法：重新计算新口令的 SHA256 hex 并替换 FixedPasswordHash。
/// 例：python3 -c "import hashlib; print(hashlib.sha256('新口令'.encode()).hexdigest())"
/// 注意：修改口令后旧 .dat 文件无法解密，需删除或迁移数据。
/// </summary>
public static class AuthService
{
    // SHA256("xct258") = faf90bcd...
    private const string FixedPasswordHash = "faf90bcdc162630ac8a5cb225cd641e7ee303c8e0e1fdf3f783784be05b7ac12";

    /// <summary>本会话验证通过的口令（内存中作为解密密钥），退出即丢失。</summary>
    public static string? SessionPassword { get; set; }

    public static bool Verify(string? inputPassword)
    {
        if (string.IsNullOrEmpty(inputPassword))
            return false;
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(inputPassword));
        string hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return string.Equals(hex, FixedPasswordHash, StringComparison.OrdinalIgnoreCase);
    }
}
