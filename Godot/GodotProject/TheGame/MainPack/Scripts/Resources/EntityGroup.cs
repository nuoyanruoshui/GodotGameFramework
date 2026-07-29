using Godot;
using System;
[GlobalClass]
public partial class EntityGroup : Resource
{
    [Export] // 资源组名称
    public string Name;
    [Export] // 资源组释放间隔
    public float ReleaseInterval;
    [Export] // 资源组容量
    public int Capacity;
    [Export] // 资源组过期时间
    public float ExpireTime;
    [Export] // 资源组优先级
    public int Priority;
}
