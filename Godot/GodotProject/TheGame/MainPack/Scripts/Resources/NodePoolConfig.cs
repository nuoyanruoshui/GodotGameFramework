using Calcatz.EzpzInspector;
using Godot;
using Godot.Collections;

/// <summary>
/// NodePool 配置资源。
/// 由 NodePoolInspectorPlugin 扫描生成，运行时由 NodePool 读取。
/// </summary>
[GlobalClass]
public partial class NodePoolConfig : Resource
{
    /// <summary>
    /// 所有池化场景条目。
    /// </summary>
    [Export]
    public Array<PoolEntry> Entries { get; set; } = new();
    [UpperDescription("若未配置，则使用全局默认值。")]
    /// <summary>
    /// 全局默认池容量（单条目未配置时使用）。
    /// </summary>
    [Export(PropertyHint.Range, "1,5000,1")]
    public int DefaultCapacity { get; set; } = 300;

    /// <summary>
    /// 全局默认对象过期时间（秒，float.MaxValue = 永不过期）。
    /// </summary>
    [Export(PropertyHint.Range, "0,3600,1")]
    public float DefaultExpireTime { get; set; } = 60f;

    /// <summary
    /// >全局默认自动释放检查间隔（秒）。
    /// </summary>
    [Export(PropertyHint.Range, "0,3600,1")]
    public float DefaultAutoReleaseInterval { get; set; } = 30f;
}
