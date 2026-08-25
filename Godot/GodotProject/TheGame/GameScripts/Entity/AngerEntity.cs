using GameConfig.Constant;
using GameConfig.Entity;
using GameFramework;
using GameFramework.Entity;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.Entity;
using GodotGameFramework.NodePool;
using System;
using System.Linq;
using System.Threading.Tasks;


public partial class AngerEntity : ActorEntity
{

    [Export]
    private HSlider m_HSlider;
    [Export]
    private int m_AttackDamage = 15;

    private float m_AttackTimer = 0f;
    private ActorEntity m_TargetPlayer = null;

    public override void OnInit(int entityId, string entityAssetName, IEntityGroup entityGroup, bool isNewInstance, object userData)
    {
        base.OnInit(entityId, entityAssetName, entityGroup, isNewInstance, userData);
        if (isNewInstance)
        {
            m_Config = ConfigSystem.Instance.Tables.TbCharacterConfig.DataList.FirstOrDefault(x => x.EntityId == EntityId.Anger);

        }
        Team = EntityTeam.Enemy;
        m_HSlider.MaxValue = ActorData.MaxHp;
        m_HSlider.Value = ActorData.Hp;
    }

    public override void OnShow(object userData)
    {
        base.OnShow(userData);

        m_AttackTimer = 0f;
        m_HSlider.Value = ActorData.Hp;
        Anim.Play("Idle");

        CollisionLayer = LayerMask.LayerToMask2D("Enemy");
    }
    public void SetTarget(ActorEntity target)
    {
        m_TargetPlayer = target;
    }
    public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        // 查找玩家
        if (m_TargetPlayer == null || !IsInstanceValid(m_TargetPlayer) || m_TargetPlayer.IsDead)
        {
            return;
        }

        float distance = GlobalPosition.DistanceTo(m_TargetPlayer.GlobalPosition);

        if (distance <= m_Config.CheckRange)
        {
            // 在攻击范围内 — 面向玩家并攻击
            FaceDirection(m_TargetPlayer.GlobalPosition - GlobalPosition);

            m_AttackTimer += elapseSeconds;
            if (m_AttackTimer >= m_Config.AtkSpeed)
            {
                m_AttackTimer = 0f;
                // _ = ShootAtPlayer();
            }
        }
        else
        {
            // 在攻击范围外 — 向玩家靠近
            Vector2 dir = (m_TargetPlayer.GlobalPosition - GlobalPosition).Normalized();
            Velocity = dir * m_Config.Speed;
            FaceDirection(dir);
            MoveAndSlide();
        }
    }

    private void FaceDirection(Vector2 dir)
    {
        if (Mathf.Abs(dir.X) > 0.01f)
        {
            if (Anim != null)
            {
                Anim.FlipH = dir.X < 0;
            }
        }
    }


    /// <summary>
    /// 朝玩家方向发射子弹
    /// </summary>
    // private async Task ShootAtPlayer()
    // {
    //     if (m_TargetPlayer == null || !IsInstanceValid(m_TargetPlayer))
    //         return;

    //     Vector2 dir = (m_TargetPlayer.GlobalPosition - GlobalPosition).Normalized();

    //     BulletData bulletData = new BulletData
    //     {
    //         Direction = dir,
    //         IsPlayerBullet = false,
    //         Speed = 250f,
    //     };

    //     var bullet = await GF.Entity.ShowEntityAsync<GanTanEntity>(EntityId.GanTan, bulletData);
    //     if (bullet != null)
    //     {
    //         bullet.Position = GlobalPosition;
    //     }
    // }

    /// <summary>
    /// 受伤时更新血条
    /// </summary>
    public override void Hurt(int entityId, int damage)
    {
        base.Hurt(entityId, damage);
        m_HSlider.Value = ActorData.Hp;
    }

    protected override async void Die()
    {
        base.Die();
        var dr = NodePool.Get<DropItem>(ResourcesCollectionConstant.Entitys_Drop);
        dr.GlobalPosition = GlobalPosition;
        dr.MoveTo(m_TargetPlayer.GlobalPosition, () =>
        {
            m_TargetPlayer.Heal(10);
        });
        GF.Archive.CurrentData.Score += 100;
        GF.Event.Fire(this, ScoreChangedEventArgs.Create(100));
        await GF.Archive.OverWriteAsync();
    }



}
