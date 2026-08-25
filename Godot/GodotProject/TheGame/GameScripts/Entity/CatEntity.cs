using GameConfig;
using GameConfig.Character;
using GameConfig.Constant;
using GameConfig.Entity;
using GameFramework;
using GameFramework.Entity;
using GameFramework.Fsm;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.Entity;
using GodotGameFramework.NodePool;
using GodotGameFramework.Sound;
using GodotGameFramework.UI;
using System;
using System.Linq;
public class IdleState : FsmState<CatEntity>
{
	protected internal override void OnEnter(IFsm<CatEntity> fsm)
	{
		fsm.Owner.Anim.Play("Idle");
		Log.Info("IdleState");
	}
	protected internal override void OnUpdate(IFsm<CatEntity> fsm, float elapseSeconds, float realElapseSeconds)
	{
		if (fsm.Owner.m_IsMoving)
		{
			ChangeState<MoveState>(fsm);
			Log.Info("m_IsMoving:");
		}
	}

}
public class MoveState : FsmState<CatEntity>
{
	protected internal override void OnEnter(IFsm<CatEntity> fsm)
	{
		fsm.Owner.Anim.Play("Walk");
		Log.Info("MoveState");
	}
	protected internal override void OnUpdate(IFsm<CatEntity> fsm, float elapseSeconds, float realElapseSeconds)
	{
		if (!fsm.Owner.m_IsMoving)
		{
			ChangeState<IdleState>(fsm);
		}
	}
}
public interface IActor
{
	void Heal(int heal);
	void Hurt(int entityId, int damage);
}
[System.Serializable]
public struct ActorData
{
	public int Hp; //生命值
	public int MaxHp; //最大生命值
}

public partial class CatEntity : ActorEntity
{
	[Export]
	private Area2D m_HitBox;
	public bool m_IsMoving;
	float m_LastAtkTime;
	private CircleShape2D m_AimShape;
	private Node2D m_ShotPos;
	private Fsm<CatEntity> m_Fsm;



	public override void OnInit(int entityId, string entityAssetName, IEntityGroup entityGroup, bool isNewInstance, object userData)
	{
		base.OnInit(entityId, entityAssetName, entityGroup, isNewInstance, userData);
		if (isNewInstance)
		{
			m_Fsm = (Fsm<CatEntity>)GF.Fsm.CreateFsm(Name, this, new IdleState(), new MoveState());
			m_Config = ConfigSystem.Instance.Tables.TbCharacterConfig.DataList.FirstOrDefault(x => x.EntityId == EntityId.Cat);
			m_ShotPos = GetNode<Node2D>("ShotPos");
			m_HitBox.BodyEntered += OnBodyEntered;
			m_Fsm.Start<IdleState>();

		}
		if (m_Check != null)
		{
			ReferencePool.Release(m_Check);
		}
		m_AimShape = new CircleShape2D();
		m_AimShape.Radius = m_Config.CheckRange;
		m_Check = PhysicsCheck2D.Create(
		this,
		m_AimShape,
		collisionMask: LayerMask.LayerToMask2D("Enemy"),
		maxResults: 16,
		collideWithBodies: true,
		collideWithAreas: false);
		Team = EntityTeam.Player;
	}

	public override void OnShow(object userData)
	{
		base.OnShow(userData);

		CollisionLayer = LayerMask.LayerToMask2D("Player");
	}

	public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
	{
		base.OnUpdate(elapseSeconds, realElapseSeconds);
		KeybordMove();
		m_LastAtkTime += realElapseSeconds;

		if (m_LastAtkTime >= 1 / m_Config.AtkSpeed)
		{
			m_LastAtkTime = 0;
			if (!m_Check.IsColliding())
				return;
			SpawnGanTan();
		}
	}
	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

	}



	/// <summary>
	/// 以玩家为中心做圆形区域检测，返回最近敌人的方向。
	/// 如果没有敌人，返回 <see cref="Vector2.Up"/>。
	/// </summary>
	private Vector2 GetAimDirection()
	{
		ActorEntity nearestEnemy = m_Check.GetCollidingNodesSorted().FirstOrDefault() as ActorEntity;
		if (nearestEnemy != null)
			return (nearestEnemy.GlobalPosition - GlobalPosition).Normalized();

		return Vector2.Up;
	}

	private async void SpawnGanTan()
	{
		Vector2 dir = GetAimDirection();

		switch (m_Config.BulletId)
		{
			case EntityId.GanTan:
				var entity1 = await GF.Entity.ShowEntityAsync<GanTanEntity>(EntityId.GanTan,
				new BulletData
				{
					Direction = dir,
					IsPlayerBullet = true,
					Speed = 300f,
				});

				if (entity1 != null)
				{
					entity1.GlobalPosition = m_ShotPos.GlobalPosition;
					GF.Sound.PlaySFX(ResourcesCollectionConstant.SFX_Shoot);
				}
				break;
			case EntityId.LightningBall:
				var entity2 = await GF.Entity.ShowEntityAsync<LightningBall>(EntityId.LightningBall,
					new BulletData
					{
						Direction = dir,
						IsPlayerBullet = true,
						Speed = 300f,
					});

				if (entity2 != null)
				{
					entity2.GlobalPosition = m_ShotPos.GlobalPosition;
					GF.Sound.PlaySFX(ResourcesCollectionConstant.SFX_Shoot);
				}
				break;
		}

	}

	private void KeybordMove()
	{
		float hor = Input.GetAxis("ui_left", "ui_right");
		float ver = Input.GetAxis("ui_up", "ui_down");
		Velocity = new Vector2(hor, ver) * m_Config.Speed;
		MoveAndSlide();

		m_IsMoving = hor != 0 || ver != 0;
		if (hor != 0) Anim.FlipH = hor < 0;

	}

	protected override void Die()
	{
		base.Die();
		GF.UI.OpenUIForm(UIFormId.GameOver);
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is ActorEntity actor && !actor.IsDead)
		{
			if (actor.Team == EntityTeam.Enemy && !IsDead)
			{
				Hurt(actor.Id, 20);
			}
		}
	}




}
