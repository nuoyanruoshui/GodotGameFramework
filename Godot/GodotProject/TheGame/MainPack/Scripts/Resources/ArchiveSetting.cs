using Godot;

[GlobalClass]
public partial class ArchiveSetting : Resource
{
    public static class Parameters
    {
        public static readonly string EnableAesEncryption = "EnableAesEncryption";
        public static readonly string KEY = "KEY";
        public static readonly string Salt = "Salt";
    }
    [Export]
    public string Folder { get; set; } = "GameData";

    [Export]
    public bool EnableAesEncryption { get; set; }

    [Export]
    public string KEY { get; set; } = "GodotGameFramework";

    [Export]
    public string Salt { get; set; } = "Rkb4jvUy/ye7Cd7k89QQgQ==";
}
