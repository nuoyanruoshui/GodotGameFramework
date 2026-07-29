using Godot;
/// <summary>
/// 单个池化场景条目。
/// </summary>
[GlobalClass]
public partial class PoolEntry : Resource
{
    /// <summary>
    /// 池化的场景模板。
    /// </summary>
    [Export]
    public string Scene { get; set; }

    /// <summary>
    /// 池容量（0 = 使用全局默认值）。
    /// </summary>
    [Export(PropertyHint.Range, "0,1000,1")]
    public int Capacity { get; set; } = 0;

    /// <summary>
    /// 对象过期时间（秒，0 = 使用全局默认值）。
    /// </summary>
    [Export(PropertyHint.Range, "0,3600,1")]
    public float ExpireTime { get; set; } = 0f;

    /// <summary>
    /// 自动释放检查间隔（秒，0 = 使用全局默认值）。
    /// </summary>
    [Export(PropertyHint.Range, "0,3600,1")]
    public float AutoReleaseInterval { get; set; } = 0f;
}
