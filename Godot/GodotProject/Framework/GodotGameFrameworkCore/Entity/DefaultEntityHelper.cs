using GameFramework.Entity;
using Godot;

namespace GodotGameFramework.Entity
{
    /// <summary>
    /// 默认实体辅助器。
    /// </summary>
    public partial class DefaultEntityHelper : EntityHelperBase
    {
        /// <summary>
        /// 实例化实体。
        /// <returns>实例化后的 Node，如果资源类型不匹配返回 null。</returns>
        public override object InstantiateEntity(object entityAsset)
        {
            if (entityAsset is PackedScene packedScene)
            {
                return packedScene.Instantiate();
            }

            Log.Warning("Entity asset is not a PackedScene: {0}.",
                entityAsset?.GetType().Name ?? "null");
            return null;
        }

        /// <summary>
        /// 创建实体。
        /// </summary>
        /// <returns>创建的 IEntity（Entity 节点）。</returns>
        public override IEntity CreateEntity(object entityInstance, IEntityGroup entityGroup, object userData)
        {
            if (entityInstance == null)
            {
                Log.Warning("Entity instance is invalid.");
                return null;
            }
            if (entityInstance is IEntity entity)
            {
                // 将 Entity 添加到实体组的容器节点下
                if (entityGroup != null && entityGroup.Helper is EntityGroupHelperBase groupHelper)
                {
                    var node = (Node)entity;
                    if (node.GetParent() != groupHelper)
                    {
                        groupHelper.AddChild((Node)entity);
                    }
                    if (node is CanvasItem canvasItem) canvasItem.MoveToFront();
                }
            }
            else
            {
                Log.Warning("Entity instance is not a EntityLogic: {0}.", entityInstance.GetType().Name);
                return null;
            }


            return entity;
        }

        /// <summary>
        /// 释放实体。
        ///
        /// 仅释放实例节点。不卸载 PackedScene 资源，因为同一资源可能被
        /// 对象池中的多个实例共享，卸载会导致其他实例失效。
        /// 资源生命周期由 Godot 引擎的资源引用计数自动管理。
        /// </summary>
        /// <param name="entityAsset">实体资源（PackedScene）。</param>
        /// <param name="entityInstance">实体实例（期望为 Node）。</param>
        public override void ReleaseEntity(object entityAsset, object entityInstance)
        {
            if (entityInstance is Node node)
            {
                node.QueueFree();
            }
        }
    }
}
