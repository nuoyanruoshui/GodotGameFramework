using Godot;

[GlobalClass]
public partial class UpdateSettingRes : Resource
{
    public static class Parameters
    {
        public static string RemoteUrl = "RemoteUrl";
        public static string HotUpdatePath = "HotUpdatePath";
    }

    /// <summary>远程更新服务器地址</summary>
    [Export]
    public string RemoteUrl = "http://127.0.0.1:8080";

    /// <summary>
    /// 热更补丁存储目录（空 = 自动选择）。
    /// 自动选择策略：游戏安装目录可写 → 放游戏目录；不可写 → 放 user://
    /// </summary>
    [Export]
    public string HotUpdatePath = "";
}
