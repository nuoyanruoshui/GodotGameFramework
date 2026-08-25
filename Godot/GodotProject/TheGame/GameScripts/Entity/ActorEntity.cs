using System;
using GameConfig.Character;
using GameConfig.Constant;
using GameFramework;
using GameFramework.Entity;
using GameFramework.Fsm;
using Godot;
using GodotGameFramework;
using GodotGameFramework.Entity;
using GodotGameFramework.NodePool;
using GodotGameFramework.Sound;


/// <summary>
/// 实体阵营
/// </summary>
public enum EntityTeam
{
    Player,
    Enemy,
}

public partial class ActorEntity : CharacterBody2D, IEntity, IActor
{
    #region Base
    /// <summary>
    /// 获取实体编号。
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// 获取实体资源名称（PackedScene 路径）。
    /// </summary>
    public string EntityAssetName { get; private set; }

    /// <summary>
    /// 获取实体实例。
    /// </summary>
    public object Handle => this;

    /// <summary>
    /// 获取实体所属的实体组。
    /// </summary>
    public IEntityGroup EntityGroup { get; private set; }
    #endregion
    public AnimatedSprite2D Anim { get; private set; }
    public ActorData ActorData;
    public bool IsDead => ActorData.Hp <= 0;
    protected CharacterConfig m_Config;
    protected PhysicsCheck2D m_Check;

    /// <summary>
    /// 实体所属阵营
    /// </summary>
    public EntityTeam Team { get; set; } = EntityTeam.Player;

    public Action<float> HpChanged;

    /// <summary>
    /// 实体初始化。
    /// </summary>
    /// <param name="entityId">实体编号。</param>
    /// <param name="entityAssetName">实体资源名称。</param>
    /// <param name="entityGroup">实体所属的实体组。</param>
    /// <param name="isNewInstance">是否是新实例。</param>
    /// <param name="userData">用户自定义数据。</param>
    public virtual void OnInit(int entityId, string entityAssetName, IEntityGroup entityGroup, bool isNewInstance, object userData)
    {
        Id = entityId;
        EntityAssetName = entityAssetName;
        Name = GameFramework.Utility.Text.Format("Entity_{0}_{1}", entityId, entityAssetName);
        EntityGroup = entityGroup;

        if (isNewInstance)
        {
            Anim = this.GetChild<AnimatedSprite2D>();

            ActorData = new ActorData()
            {
                MaxHp = 100,
                Hp = 100
            };
        }

        ActorData.MaxHp = 100;
        ActorData.Hp = ActorData.MaxHp;
    }

    /// <summary>
    /// 实体回收。
    /// Entity 节点不销毁，等待对象池复用或池释放。
    /// </summary>
    public virtual void OnRecycle()
    {
        Id = 0;
        EntityAssetName = null;
        Name = "Entity (Recycled)";
        Visible = false;
        Position = Vector2.Zero;
        Rotation = 0;
        Velocity = Vector2.Zero;
    }

    /// <summary>
    /// 实体显示。
    /// </summary>
    public virtual void OnShow(object userData)
    {
        Visible = true;

    }

    /// <summary>
    /// 实体隐藏。
    /// </summary>
    public virtual void OnHide(bool isShutdown, object userData)
    {
        Visible = false;
        CollisionLayer = LayerMask.LayerToMask2D("Ignore");
    }

    /// <summary>
    /// 实体附加子实体。
    /// </summary>
    public virtual void OnAttached(IEntity childEntity, object userData)
    {

    }

    /// <summary>
    /// 实体解除子实体。
    /// </summary>
    public virtual void OnDetached(IEntity childEntity, object userData)
    {

    }

    /// <summary>
    /// 实体被附加到父实体。
    /// </summary>
    public virtual void OnAttachTo(IEntity parentEntity, object userData)
    {

    }

    /// <summary>
    /// 实体从父实体解除。
    /// </summary>
    public virtual void OnDetachFrom(IEntity parentEntity, object userData)
    {

    }

    /// <summary>
    /// 实体轮询。
    /// 每帧调用，用于处理实体逻辑。
    /// </summary>
    public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
#if TOOLS
        QueueRedraw();
#endif
    }



    /// <summary>
    /// 受伤
    /// </summary>
    /// <param name="entityId">攻击者实体编号</param>
    /// <param name="damage">伤害值</param>
    public virtual void Hurt(int entityId, int damage)
    {
        ActorData.Hp -= damage;
        ActorData.Hp = Mathf.Max(0, ActorData.Hp);
        HpChanged?.Invoke(ActorData.Hp);
        GF.Sound.PlaySFX(ResourcesCollectionConstant.SFX_Dead);
        var d = NodePool.Get<DamagePop>(ResourcesCollectionConstant.UIs_DamagePop, this);
        d?.SetText(GlobalPosition, 20, Colors.Red);
        Log.Debug("{0} 受到 {1} 点伤害，剩余 HP: {2}", Name, damage, ActorData.Hp);

        if (ActorData.Hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 治疗
    /// </summary>
    public void Heal(int heal)
    {
        ActorData.Hp += heal;
        ActorData.Hp = Mathf.Min(ActorData.Hp, ActorData.MaxHp);
        HpChanged?.Invoke(ActorData.Hp);
        if (ActorData.Hp > ActorData.MaxHp)
        {
            ActorData.Hp = ActorData.MaxHp;
        }
    }
    public override void _Draw()
    {
        base._Draw();
        if (m_Config != null)
        {
            DrawCircle(Vector2.Zero, m_Config.CheckRange, Colors.Red, false, 2f);
        }
        m_Check?.DrawDebugLines(Colors.Green);
    }
    /// <summary>
    /// 死亡
    /// </summary>
    protected virtual void Die()
    {
        GF.Entity.HideEntitySafe(this);
    }
    public override void _ExitTree()
    {
        base._ExitTree();
        if (m_Check != null)
        {
            ReferencePool.Release(m_Check);
            m_Check = null;
        }
    }


}
