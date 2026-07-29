using Godot;
using System;
using GameFramework.ObjectPool;
using GameFramework;
namespace GodotGameFramework.NodePool;

public partial class NodeObject : ObjectBase
{
    public static NodeObject Create(string name, Node node)
    {
        NodeObject obj = ReferencePool.Acquire<NodeObject>();
        obj.Initialize(name, node);
        return obj;
    }
    protected internal override void Release(bool isShutdown)
    {
        if (Target is Node node)
        {
            node.QueueFree();
        }
    }

}
