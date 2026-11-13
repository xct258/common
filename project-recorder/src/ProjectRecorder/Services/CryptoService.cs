using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRecorder.Services;

/// <summary>
/// AES-256 加密服务。密钥 = SHA256(密码)，每文件随机 16 字节 IV，文件格式 = IV + Ciphertext。
/// </summary>
public static class CryptoService
{
    public static byte[] DeriveKey(string password)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
    }

    public static void EncryptToFile(string plainText, string filePath, string password)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
        EncryptBytesToFile(plain, filePath, password);
    }

    public static void EncryptBytesToFile(byte[] plain, string filePath, string password)
    {
        byte[] key = DeriveKey(password);
        byte[] data = plain ?? Array.Empty<byte>();

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using ICryptoTransform enc = aes.CreateEncryptor();
        byte[] cipher = enc.TransformFinalBlock(data, 0, data.Length);

        byte[] outBytes = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, outBytes, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, outBytes, aes.IV.Length, cipher.Length);
        File.WriteAllBytes(filePath, outBytes);
    }

    public static string DecryptFromFile(string filePath, string password)
    {
        byte[] all = File.ReadAllBytes(filePath);
        return Encoding.UTF8.GetString(DecryptToBytes(all, password));
    }

    public static byte[] DecryptFileToBytes(string filePath, string password)
    {
        return DecryptToBytes(File.ReadAllBytes(filePath), password);
    }

    public static byte[] DecryptToBytes(byte[] all, string password)
    {
        if (all == null || all.Length < 17)
            throw new CryptographicException("数据文件损坏");

        byte[] key = DeriveKey(password);
        byte[] iv = new byte[16];
        byte[] cipher = new byte[all.Length - 16];
        Buffer.BlockCopy(all, 0, iv, 0, 16);
        Buffer.BlockCopy(all, 16, cipher, 0, cipher.Length);

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using ICryptoTransform dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    public static string DecryptBytes(byte[] all, string password)
    {
        return Encoding.UTF8.GetString(DecryptToBytes(all, password));
    }
}
