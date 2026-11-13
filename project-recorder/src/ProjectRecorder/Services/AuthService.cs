using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRecorder.Services;

/// <summary>
/// 密码验证：首次运行时由用户设置密码，密码不硬编码。
/// 校验值（随机盐 + SHA256(盐 + 密码)）明文存 Data/auth.dat（仅用于校验，不等于解密密钥，泄露也无法反推密钥）。
/// 密码同时是底层 .dat 数据的解密密钥；统一用 Normalize 后的小写形式。
/// 注意：修改密码后旧 .dat 文件无法解密。
/// </summary>
public static class AuthService
{
    /// <summary>本会话验证通过的密码（内存中作为解密密钥），退出即丢失。</summary>
    public static string? SessionPassword { get; set; }

    /// <summary>密码校验值文件（存于数据目录）。</summary>
    public static string VerifierPath => Path.Combine(DataStore.DataDir, "auth.dat");

    /// <summary>是否已设置过密码（首次打开需先设置）。</summary>
    public static bool HasPassword => File.Exists(VerifierPath);

    /// <summary>密码规范形式：去首尾空白 + 转小写。登录、校验、解密都用它。</summary>
    public static string Normalize(string? inputPassword)
        => (inputPassword ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>设置（或重置）密码：生成随机盐并写入校验值文件。</summary>
    public static void SetPassword(string? inputPassword)
    {
        string normalized = Normalize(inputPassword);
        if (normalized.Length == 0)
            throw new ArgumentException("密码不能为空。", nameof(inputPassword));

        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);
        byte[] hash = Hash(normalized, salt);

        Directory.CreateDirectory(DataStore.DataDir);
        string content = Convert.ToBase64String(salt) + "\n" + Convert.ToBase64String(hash);
        File.WriteAllText(VerifierPath, content, Encoding.UTF8);
    }

    /// <summary>校验密码（读 Data/auth.dat 的盐值重算哈希）。未设置密码时返回 false。</summary>
    public static bool Verify(string? inputPassword)
    {
        if (string.IsNullOrEmpty(inputPassword) || !HasPassword) return false;
        try
        {
            string[] lines = File.ReadAllLines(VerifierPath);
            if (lines.Length < 2) return false;
            byte[] salt = Convert.FromBase64String(lines[0]);
            byte[] expect = Convert.FromBase64String(lines[1]);
            byte[] actual = Hash(Normalize(inputPassword), salt);
            return FixedEquals(actual, expect);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Hash(string normalized, byte[] salt)
    {
        byte[] pw = Encoding.UTF8.GetBytes(normalized);
        byte[] buffer = new byte[salt.Length + pw.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(pw, 0, buffer, salt.Length, pw.Length);
        using var sha = SHA256.Create();
        return sha.ComputeHash(buffer);
    }

    private static bool FixedEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
