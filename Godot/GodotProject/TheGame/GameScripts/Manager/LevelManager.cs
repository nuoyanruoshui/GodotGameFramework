using GameConfig;
using GameConfig.Constant;
using GameConfig.Entity;
using GameConfig.Level;
using GameLogic;
using Godot;
using GodotGameFramework;
using GodotGameFramework.Entity;
using GodotGameFramework.NodePool;
using GodotGameFramework.UI;
using GodotGameFrameworkCore.SingletonSystem;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class LevelManager : SingletonNode<LevelManager>
{
    private MainForm m_MainForm;
    private LevelConfig m_LevelConfig;
    private Node2D m_Scene;
    private Line2D m_Line2D;
    public CatEntity Cat { get; private set; }
    public int WaveIndex { get; private set; }
    public string Level { get; private set; }
    public int Timer => 15 * Mathf.Min(m_LevelConfig.Waves.Length, 1);
    public async Task StartLevel(string level)
    {
        var loading = await GF.UI.OpenLoadingUIFormAsync();
        try
        {
            m_LevelConfig = ConfigSystem.Instance.Tables.TbLevelConfig.DataList.FirstOrDefault(x => x.Level == level);
            m_Scene = (Node2D)await GF.Scene.LoadSceneAsync(m_LevelConfig.Map);
            Node2D spawnPoint = m_Scene.GetNode<Node2D>("SpawnPoint");
            m_Line2D = m_Scene.GetNode<Line2D>("Line2D");
            Cat = await GF.Entity.ShowEntityAsync<CatEntity>(EntityId.Cat);
            Cat.GlobalPosition = spawnPoint.GlobalPosition;
            StartWave();
            Level = level;
            m_MainForm = await GF.UI.OpenUIFormAsync<MainForm>(UIFormId.MainForm);

            //节点使用
            GF.DataNode.SetData(nameof(MainForm), (VarInt32)m_MainForm.SerialId);
            GF.DataNode.SetData("Scene", (VarString)m_LevelConfig.Map);
        }
        finally
        {
            // 加载完成（或中途异常）时显式收掉加载遮罩，遮罩全程覆盖场景/实体/主界面加载
            loading?.CloseLoading();
        }
    }

    public async void StartWave(int waveIndex = 0)
    {
        WaveIndex = Math.Min(waveIndex, m_LevelConfig.Waves.Length - 1);
        int pointsCount = Random.Shared.Next(5, Mathf.Min(5, m_LevelConfig.Waves.Length - 1));
        var wave = m_LevelConfig.Waves[WaveIndex];
        for (int i = 0; i < pointsCount; i++)
        {
            var point = m_Line2D.Points[Random.Shared.Next(m_Line2D.Points.Length)];
            for (int j = 0; j < wave.Num; j++)
            {
                var enemy = await GF.Entity.ShowEntityAsync<AngerEntity>(EntityId.Anger);
                enemy.GlobalPosition = point;
                enemy.SetTarget(Cat);
            }
        }
    }

    public void NextWave()
    {
        WaveIndex++;
        StartWave(WaveIndex);
    }


    public void ExitGame()
    {
        GF.UI.CloseUIForm(m_MainForm);
        GF.UI.OpenUIForm(UIFormId.MenuForm);
        GF.Entity.HideAllLoadedEntities();
        NodePool.ReleaseAll();
        GF.Scene.UnloadScene(m_LevelConfig.Map);
    }


}
