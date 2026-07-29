using Godot;
namespace GodotGameFramework.NodePool;
/// <summary>
/// 池容器节点，作为池中所有对象在场景树中的父节点。
/// 归还对象时重新挂载到此节点下。
/// </summary>
public partial class PoolContainer : Node
{
    public string PoolName { get; set; }
    public PoolContainer(string poolName)
    {
        PoolName = poolName;
        Name = poolName;
    }
}
