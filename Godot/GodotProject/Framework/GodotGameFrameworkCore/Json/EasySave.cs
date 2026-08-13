using Godot;
using System;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;
using GameFramework.Resource;
using GodotGameFramework.Archive;

namespace GodotGameFramework.Json;

/// <summary>
/// 轻量级 JSON 持久化工具。
/// 所有路径在使用前通过 ProjectSettings.GlobalizePath 解析。
/// 注意：Godot 文件 API（FileAccess）不是线程安全的，
/// 因此异步方法内部使用纯 .NET 的 StreamWriter/StreamReader + Task.Run。
/// </summary>
public static class EasySave
{
    private static readonly string s_UserDir = ProjectSettings.GlobalizePath("user://");
    private static readonly string s_ProjectDir = ProjectSettings.GlobalizePath("res://");

    // ──────────────────────────
    //  同步方法（主线程安全）
    // ──────────────────────────

    public static bool TrySave<T>(T data, string filePath)
    {
        try
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            writer.Write(json);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 保存失败: {filePath} — {ex.Message}");
            return false;
        }
    }

    public static T LoadOrDefault<T>(string filePath) where T : class, new()
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
            string json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 加载失败: {filePath} — {ex.Message}");
            return null;
        }
    }

    public static bool TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 删除失败: {filePath} — {ex.Message}");
            return false;
        }
    }

    public static bool FileExists(string filePath) => File.Exists(filePath);

    // ──────────────────────────
    //  user:// 便捷方法
    // ──────────────────────────

    public static bool SaveInUser<T>(T data, string fileName) =>
        TrySave(data, Path.Combine(s_UserDir, fileName));

    public static T LoadFromUser<T>(string fileName) where T : class, new() =>
        LoadOrDefault<T>(Path.Combine(s_UserDir, fileName));

    public static bool DeleteInUser(string fileName) =>
        TryDelete(Path.Combine(s_UserDir, fileName));

    public static bool ExistsInUser(string fileName) =>
        File.Exists(Path.Combine(s_UserDir, fileName));



    // ──────────────────────────
    //  res:// 便捷方法
    // ──────────────────────────

    public static bool SaveInProject<T>(T data, string fileName) =>
        TrySave(data, Path.Combine(s_ProjectDir, fileName));

    public static T LoadFromProject<T>(string fileName) where T : class, new() =>
        LoadOrDefault<T>(Path.Combine(s_ProjectDir, fileName));

    public static bool DeleteInProject(string fileName) =>
        TryDelete(Path.Combine(s_ProjectDir, fileName));
    public static bool ExistsInProject(string fileName) =>
            File.Exists(Path.Combine(s_ProjectDir, fileName));


    // ──────────────────────────
    //  异步方法（用于非 Godot 线程的调用场景）
    // ──────────────────────────

    public static async Task<bool> SaveInUserAsync<T>(T data, string fileName)
    {
        string path = Path.Combine(s_UserDir, fileName);
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);

            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
                writer.Write(json);
            });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 异步保存失败: {path} — {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> SaveInUserAsync<T>(T data, string fileName, bool encrypt, string key, string salt)
    {
        string path = Path.Combine(s_UserDir, fileName);
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = encrypt ? Rijindael.Encrypt(JsonConvert.SerializeObject(data, Formatting.Indented), key, salt) : JsonConvert.SerializeObject(data, Formatting.Indented);
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
                writer.Write(json);
            });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 异步保存失败: {path} — {ex.Message}");
            return false;
        }
    }

    public static async Task<T> LoadFromUserAsync<T>(string fileName) where T : class, new()
    {
        string path = Path.Combine(s_UserDir, fileName);
        try
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
                string json = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<T>(json);
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 异步加载失败: {path} — {ex.Message}");
            return null;
        }
    }

    public static async Task<T> LoadFromUserAsync<T>(string fileName, bool encrypt, string key, string salt) where T : class, new()
    {
        string path = Path.Combine(s_UserDir, fileName);
        try
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
                string json = encrypt ? Rijindael.Decrypt(reader.ReadToEnd(), key, salt) : reader.ReadToEnd();
                return JsonConvert.DeserializeObject<T>(json);
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 异步加载失败: {path} — {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> DeleteInUserAsync(string fileName)
    {
        string path = Path.Combine(s_UserDir, fileName);
        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(path))
                    File.Delete(path);
            });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EasySave] 异步删除失败: {path} — {ex.Message}");
            return false;
        }
    }

}
