using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GodotGameFramework.Archive;

/// <summary>
/// 存档数据加密工具（AES-256-CBC + PBKDF2 密钥派生 + 每次随机 IV）。
/// 密文格式：Base64( 16 字节随机 IV || AES 密文 )。
/// 纯 .NET 实现，不依赖 Godot，可在后台线程安全调用。
/// </summary>
public static class Rijindael
{
    private const CipherMode DefaultCipherMode = CipherMode.CBC;
    private const PaddingMode DefaultPaddingMode = PaddingMode.PKCS7;
    private const int DefaultKeySize = 256; // AES-256
    private const int IvSize = 16;          // AES 块大小（字节）
    private const int Pbkdf2Iterations = 10000;

    /// <summary>
    /// 加密字符串。key 为口令，salt 用于 PBKDF2 派生密钥，IV 每次随机生成并前置写入密文。
    /// </summary>
    public static string Encrypt(string plainText, string key, string salt)
    {
        ValidateInput(plainText, key, salt);

        byte[] derivedKey = DeriveKey(key, salt);

        using var aes = Aes.Create();
        ConfigureAlgorithm(aes);
        aes.Key = derivedKey;
        aes.GenerateIV(); // 每次随机 IV，避免相同明文产生相同密文

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }

        // 前置 IV + 密文，整体 Base64 编码，解密时按前 16 字节还原 IV
        byte[] iv = aes.IV;
        byte[] cipher = ms.ToArray();
        byte[] output = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, output, iv.Length, cipher.Length);
        return Convert.ToBase64String(output);
    }

    /// <summary>
    /// 解密字符串。失败时抛出 <see cref="CryptoException"/>，由调用方统一捕获。
    /// </summary>
    public static string Decrypt(string cipherText, string key, string salt)
    {
        if (string.IsNullOrEmpty(cipherText))
            throw new CryptoException("密文为空");

        ValidateKeyAndSalt(key, salt);
        byte[] derivedKey = DeriveKey(key, salt);

        byte[] allBytes;
        try
        {
            allBytes = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new CryptoException("无效的 Base64 密文", ex);
        }

        if (allBytes.Length <= IvSize)
            throw new CryptoException("密文长度非法");

        byte[] iv = new byte[IvSize];
        byte[] cipher = new byte[allBytes.Length - IvSize];
        Buffer.BlockCopy(allBytes, 0, iv, 0, IvSize);
        Buffer.BlockCopy(allBytes, IvSize, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        ConfigureAlgorithm(aes);
        aes.Key = derivedKey;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// 生成随机盐值（16 字节随机数，Base64 编码）。供编辑器「随机生成」按钮调用。
    /// </summary>
    public static string GenerateIV()
    {
        byte[] bytes = new byte[IvSize];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static void ConfigureAlgorithm(SymmetricAlgorithm algorithm)
    {
        algorithm.Mode = DefaultCipherMode;
        algorithm.Padding = DefaultPaddingMode;
        algorithm.KeySize = DefaultKeySize;
    }

    private static byte[] DeriveKey(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            Encoding.UTF8.GetBytes(salt),
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(DefaultKeySize / 8);
    }

    private static void ValidateInput(string text, string key, string salt)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentNullException(nameof(text));

        ValidateKeyAndSalt(key, salt);
    }

    private static void ValidateKeyAndSalt(string key, string salt)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (string.IsNullOrEmpty(salt) || Encoding.UTF8.GetByteCount(salt) < 8)
            throw new ArgumentException("盐值至少需要 8 字节（PBKDF2 要求）");
    }
}

/// <summary>
/// 加密/解密失败时抛出，由调用方（如 EasySave）统一捕获处理。
/// </summary>
public class CryptoException : Exception
{
    public CryptoException(string message) : base(message) { }

    public CryptoException(string message, Exception inner) : base(message, inner) { }
}
