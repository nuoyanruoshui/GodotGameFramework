using GameFramework;
using GameFramework.Entity;
using GameFramework.ObjectPool;
using GameFramework.Resource;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GodotGameFramework.Entity
{
    /// <summary>
    /// 实体组件。
    /// </summary>
    public sealed partial class EntityComponent : GameFrameworkComponent
    {
        public static class Parameters
        {
            public static readonly string EntityHelper = "m_EntityHelperTypeName";
            public static readonly string EntityGroupHelper = "m_EntityGroupHelperTypeName";
        }
        private const int DefaultPriority = 0;

        private IEntityManager m_EntityManager = null;
        private EventComponent m_EventComponent = null;
        private EntityHelperBase m_EntityHelper = null;

        [Export] private bool m_EnableShowEntitySuccessEvent = true;
        [Export] private bool m_EnableShowEntityFailureEvent = true;
        [Export] private bool m_EnableShowEntityUpdateEvent = false;
        [Export] private bool m_EnableHideEntityCompleteEvent = true;
        [Export] private float m_InstanceAutoReleaseInterval = 60f;
        [Export] private int m_InstanceCapacity = 16;
        private float m_InstanceExpireTime = 60f;
        [Export] private int m_InstancePriority = 0;
        [Export] private string m_EntityHelperTypeName = "GodotGameFramework.Entity.DefaultEntityHelper";
        [Export] private string m_EntityGroupHelperTypeName = "GodotGameFramework.Entity.DefaultEntityGroupHelper";
        [Export]
        public EntityGroupRes EntityGroupRes;
        public int EntityCount => m_EntityManager.EntityCount;
        public int EntityGroupCount => m_EntityManager.EntityGroupCount;

        private readonly Dictionary<int, TaskCompletionSource<IEntity>> m_LoadingTasks = new();

        public override void OnInit()
        {
            base.OnInit();

            m_EntityManager = GameFrameworkEntry.GetModule<IEntityManager>();
            if (m_EntityManager == null) { Log.Fatal("Entity manager is invalid."); return; }

            m_EventComponent = GameEntry.GetComponent<EventComponent>();
            if (m_EventComponent == null) { Log.Fatal("Event component is invalid."); return; }

            if (m_EnableShowEntitySuccessEvent) m_EntityManager.ShowEntitySuccess += OnShowEntitySuccess;
            m_EntityManager.ShowEntityFailure += OnShowEntityFailure;
            if (m_EnableShowEntityUpdateEvent) m_EntityManager.ShowEntityUpdate += OnShowEntityUpdate;
            if (m_EnableHideEntityCompleteEvent) m_EntityManager.HideEntityComplete += OnHideEntityComplete;

            m_EntityManager.SetResourceManager(GameFrameworkEntry.GetModule<IResourceManager>());
            m_EntityManager.SetObjectPoolManager(GameFrameworkEntry.GetModule<IObjectPoolManager>());
            m_EntityHelper = (EntityHelperBase)Create(m_EntityHelperTypeName);
            if (m_EntityHelper == null) { Log.Fatal("Can not create entity helper."); return; }
            m_EntityHelper.Name = m_EntityHelperTypeName;
            m_EntityManager.SetEntityHelper(m_EntityHelper);
            AddChild(m_EntityHelper);
        }


        public override void OnExitTree()
        {
            if (m_EntityManager != null)
            {
                m_EntityManager.ShowEntitySuccess -= OnShowEntitySuccess;
                m_EntityManager.ShowEntityFailure -= OnShowEntityFailure;
                m_EntityManager.ShowEntityUpdate -= OnShowEntityUpdate;
                m_EntityManager.HideEntityComplete -= OnHideEntityComplete;
            }
            // 关闭时补全所有挂起的实体加载任务，避免 await 调用方永久挂起
            foreach (TaskCompletionSource<IEntity> tcs in m_LoadingTasks.Values)
            {
                tcs.TrySetCanceled();
            }
            m_LoadingTasks.Clear();
            base.OnExitTree();
        }

        // ================================================================
        //  实体组管理
        // ================================================================

        /// <summary>是否存在指定名称的实体组。</summary>
        public bool HasEntityGroup(string entityGroupName)
        {
            if (string.IsNullOrEmpty(entityGroupName)) throw new GameFrameworkException("Entity group name is invalid.");
            return m_EntityManager.HasEntityGroup(entityGroupName);
        }

        /// <summary>获取指定名称的实体组。</summary>
        public IEntityGroup GetEntityGroup(string entityGroupName)
        {
            if (string.IsNullOrEmpty(entityGroupName)) throw new GameFrameworkException("Entity group name is invalid.");
            return m_EntityManager.GetEntityGroup(entityGroupName);
        }

        /// <summary>获取所有实体组。</summary>
        public IEntityGroup[] GetAllEntityGroups() => m_EntityManager.GetAllEntityGroups();

        /// <summary>获取所有实体组，写入已有列表。</summary>
        public void GetAllEntityGroups(List<IEntityGroup> results) => m_EntityManager.GetAllEntityGroups(results);

        /// <summary>增加实体组。创建容器节点并委托核心管理器初始化对象池。</summary>
        public bool AddEntityGroup(string entityGroupName, float instanceAutoReleaseInterval,
            int instanceCapacity, float instanceExpireTime, int instancePriority)
        {
            if (string.IsNullOrEmpty(entityGroupName)) throw new GameFrameworkException("Entity group name is invalid.");
            if (m_EntityManager.HasEntityGroup(entityGroupName)) return false;

            EntityGroupHelperBase entityGroup = Create(m_EntityGroupHelperTypeName) as EntityGroupHelperBase;
            if (entityGroup == null) { Log.Fatal("Failed to create entity group helper."); return false; }
            entityGroup.Name = Utility.Text.Format("{0}-{1}", m_EntityGroupHelperTypeName, entityGroupName);
            AddChild(entityGroup);
            return m_EntityManager.AddEntityGroup(entityGroupName, instanceAutoReleaseInterval,
                instanceCapacity, instanceExpireTime, instancePriority, entityGroup);
        }

        // ================================================================
        //  显示实体
        // ================================================================

        /// <summary>显示实体，通过泛型参数指定 EntityLogic 类型。</summary>
        public void ShowEntity(int entityId, string entityAssetName,
            string entityGroupName, object userData = null)
        {
            m_EntityManager.ShowEntity(entityId, entityAssetName, entityGroupName, DefaultPriority, userData);
        }

        /// <summary>显示实体，指定加载优先级。</summary>
        public void ShowEntity(int entityId, string entityAssetName,
            string entityGroupName, int priority, object userData = null)
        {
            m_EntityManager.ShowEntity(entityId, entityAssetName, entityGroupName, priority, userData);
        }


        // ================================================================
        //  异步显示实体
        // ================================================================

        /// <summary>异步显示实体，通过泛型参数指定 EntityLogic 类型。返回 Task&lt;IEntity&gt;。</summary>
        public async Task<IEntity> ShowEntityAsync(int entityId, string entityAssetName,
            string entityGroupName, object userData = null)
        {
            return await ShowEntityAsyncInternal(entityId, entityAssetName, entityGroupName, DefaultPriority, userData);
        }

        /// <summary>异步显示实体，指定加载优先级。返回 Task&lt;IEntity&gt;。</summary>
        public async Task<IEntity> ShowEntityAsync(int entityId, string entityAssetName,
            string entityGroupName, int priority, object userData = null)
        {
            return await ShowEntityAsyncInternal(entityId, entityAssetName, entityGroupName, priority, userData);
        }

        /// <summary>
        /// 异步显示实体内部实现。通过 TaskCompletionSource 桥接 IEntityManager 事件。
        /// </summary>
        private Task<IEntity> ShowEntityAsyncInternal(int entityId, string entityAssetName,
            string entityGroupName, int priority, object userData)
        {
            if (string.IsNullOrEmpty(entityAssetName))
                return Task.FromException<IEntity>(new GameFrameworkException("Entity asset name is invalid."));
            if (string.IsNullOrEmpty(entityGroupName))
                return Task.FromException<IEntity>(new GameFrameworkException("Entity group name is invalid."));

            var tcs = new TaskCompletionSource<IEntity>();
            m_LoadingTasks.TryAdd(entityId, tcs);
            try
            {
                m_EntityManager.ShowEntity(entityId, entityAssetName, entityGroupName, priority, userData);
            }
            catch (Exception ex)
            {
                m_LoadingTasks.Remove(entityId);
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        /// <summary>隐藏实体。</summary>
        public void HideEntity(int entityId, object userData = null) => m_EntityManager.HideEntity(entityId, userData);

        /// <summary>隐藏实体。</summary>
        public void HideEntity(IEntity entity, object userData = null) => m_EntityManager.HideEntity(entity, userData);

        /// <summary>隐藏所有已加载的实体。</summary>
        public void HideAllLoadedEntities(object userData = null) => m_EntityManager.HideAllLoadedEntities(userData);

        /// <summary>隐藏所有正在加载的实体。</summary>
        public void HideAllLoadingEntities() => m_EntityManager.HideAllLoadingEntities();

        // ================================================================
        //  实体查询
        // ================================================================

        /// <summary>是否存在指定编号的实体。</summary>
        public bool HasEntity(int entityId) => m_EntityManager.HasEntity(entityId);

        /// <summary>是否存在指定资源名称的实体。</summary>
        public bool HasEntity(string entityAssetName) => m_EntityManager.HasEntity(entityAssetName);

        /// <summary>获取指定编号的实体。</summary>
        public IEntity GetEntity(int entityId) => m_EntityManager.GetEntity(entityId);

        /// <summary>获取指定资源名称的实体（返回第一个匹配项）。</summary>
        public IEntity GetEntity(string entityAssetName) => m_EntityManager.GetEntity(entityAssetName);

        /// <summary>获取所有匹配资源名称的实体。</summary>
        public IEntity[] GetEntities(string entityAssetName) => m_EntityManager.GetEntities(entityAssetName);

        /// <summary>获取所有已加载的实体。</summary>
        public IEntity[] GetAllLoadedEntities() => m_EntityManager.GetAllLoadedEntities();

        /// <summary>获取所有已加载的实体，写入已有列表。</summary>
        public void GetAllLoadedEntities(List<IEntity> results) => m_EntityManager.GetAllLoadedEntities(results);

        /// <summary>实体是否有效。</summary>
        public bool IsValidEntity(IEntity entity) => m_EntityManager.IsValidEntity(entity);

        /// <summary>实体是否正在加载中。</summary>
        public bool IsLoadingEntity(int entityId) => m_EntityManager.IsLoadingEntity(entityId);

        /// <summary>获取所有正在加载实体的编号。</summary>
        public int[] GetAllLoadingEntityIds() => m_EntityManager.GetAllLoadingEntityIds();

        // ================================================================
        //  父子实体
        // ================================================================

        /// <summary>附加子实体到父实体。同时处理 Godot 场景树 Node 父子关系。</summary>
        public void AttachEntity(int childEntityId, int parentEntityId, object userData = null)
        {
            DetachEntity(childEntityId, userData);
            m_EntityManager.AttachEntity(childEntityId, parentEntityId, userData);

            IEntity childEntity = m_EntityManager.GetEntity(childEntityId);
            IEntity parentEntity = m_EntityManager.GetEntity(parentEntityId);
            if (childEntity is Node childNode && parentEntity is Node parentNode)
            {
                Node originalParent = childNode.GetParent();
                if (originalParent != null && originalParent != parentNode) originalParent.RemoveChild(childNode);
                if (childNode.GetParent() != parentNode) parentNode.AddChild(childNode);
            }
        }

        /// <summary>附加子实体到父实体。</summary>
        public void AttachEntity(int childEntityId, IEntity parentEntity, object userData = null)
        {
            if (parentEntity == null) throw new GameFrameworkException("Parent entity is invalid.");
            AttachEntity(childEntityId, parentEntity.Id, userData);
        }

        /// <summary>附加子实体到父实体。</summary>
        public void AttachEntity(IEntity childEntity, int parentEntityId, object userData = null)
        {
            if (childEntity == null) throw new GameFrameworkException("Child entity is invalid.");
            AttachEntity(childEntity.Id, parentEntityId, userData);
        }

        /// <summary>附加子实体到父实体。</summary>
        public void AttachEntity(IEntity childEntity, IEntity parentEntity, object userData = null)
        {
            if (childEntity == null) throw new GameFrameworkException("Child entity is invalid.");
            if (parentEntity == null) throw new GameFrameworkException("Parent entity is invalid.");
            AttachEntity(childEntity.Id, parentEntity.Id, userData);
        }

        /// <summary>解除子实体的父子关系。将子 Node 移回所属实体组容器。</summary>
        public void DetachEntity(int childEntityId, object userData = null)
        {
            IEntity childEntity = m_EntityManager.GetEntity(childEntityId);
            m_EntityManager.DetachEntity(childEntityId, userData);

            if (childEntity is Node childNode)
            {
                IEntityGroup group = childEntity.EntityGroup;
                if (group?.Helper is DefaultEntityGroupHelper groupHelper)
                {
                    Node currentParent = childNode.GetParent();
                    if (currentParent != null && currentParent != groupHelper)
                    {
                        currentParent.RemoveChild(childNode);
                        groupHelper.AddChild(childNode);
                    }
                }
            }
        }

        /// <summary>解除子实体的父子关系。</summary>
        public void DetachEntity(IEntity childEntity, object userData = null)
        {
            if (childEntity == null) throw new GameFrameworkException("Child entity is invalid.");
            DetachEntity(childEntity.Id, userData);
        }

        /// <summary>解除父实体的所有子实体。</summary>
        public void DetachChildEntities(int parentEntityId, object userData = null) =>
            m_EntityManager.DetachChildEntities(parentEntityId, userData);

        /// <summary>解除父实体的所有子实体。</summary>
        public void DetachChildEntities(IEntity parentEntity, object userData = null) =>
            m_EntityManager.DetachChildEntities(parentEntity ?? throw new GameFrameworkException("Parent entity is invalid."), userData);

        /// <summary>获取父实体。</summary>
        public IEntity GetParentEntity(int childEntityId) => m_EntityManager.GetParentEntity(childEntityId);

        /// <summary>获取父实体。</summary>
        public IEntity GetParentEntity(IEntity childEntity) => m_EntityManager.GetParentEntity(childEntity);

        /// <summary>获取子实体数量。</summary>
        public int GetChildEntityCount(int parentEntityId) => m_EntityManager.GetChildEntityCount(parentEntityId);

        /// <summary>获取第一个子实体。</summary>
        public IEntity GetChildEntity(int parentEntityId) => m_EntityManager.GetChildEntity(parentEntityId);

        /// <summary>获取所有子实体。</summary>
        public IEntity[] GetChildEntities(int parentEntityId) => m_EntityManager.GetChildEntities(parentEntityId);

        /// <summary>获取所有子实体。</summary>
        public IEntity[] GetChildEntities(IEntity parentEntity) => m_EntityManager.GetChildEntities(parentEntity);

        private void OnShowEntitySuccess(object sender, ShowEntitySuccessEventArgs e)
        {
            // 管理器会在回调返回后立即回收 e，入事件池队列必须复制一份，否则会双重归还
            m_EventComponent.Fire(this, ShowEntitySuccessEventArgs.Create(e.Entity, e.Duration, e.UserData));
            if (m_LoadingTasks.ContainsKey(e.Entity.Id))
            {
                m_LoadingTasks[e.Entity.Id].SetResult(e.Entity);
                m_LoadingTasks.Remove(e.Entity.Id);
            }
        }

        private void OnShowEntityFailure(object sender, ShowEntityFailureEventArgs e)
        {
            Log.Warning("Show entity failure, asset '{0}', group '{1}', msg '{2}'.",
                e.EntityAssetName, e.EntityGroupName, e.ErrorMessage);
            if (m_EnableShowEntityFailureEvent)
            {
                m_EventComponent.Fire(this, ShowEntityFailureEventArgs.Create(e.EntityId, e.EntityAssetName, e.EntityGroupName, e.ErrorMessage, e.UserData));
            }
            if (m_LoadingTasks.ContainsKey(e.EntityId))
            {
                m_LoadingTasks[e.EntityId].SetException(new GameFrameworkException(e.ErrorMessage));
                m_LoadingTasks.Remove(e.EntityId);
            }
        }

        private void OnShowEntityUpdate(object sender, GameFramework.Entity.ShowEntityUpdateEventArgs e)
        {
            m_EventComponent.Fire(this, ShowEntityUpdateEventArgs.Create(e));
        }
        private void OnHideEntityComplete(object sender, HideEntityCompleteEventArgs e)
        {
            m_EventComponent.Fire(this, HideEntityCompleteEventArgs.Create(e.EntityId, e.EntityAssetName, e.EntityGroup, e.UserData));
        }
    }
}
