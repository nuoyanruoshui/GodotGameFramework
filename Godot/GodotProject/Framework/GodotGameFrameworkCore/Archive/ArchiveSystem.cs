using GodotGameFramework.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GameConfig.Constant;

namespace GodotGameFramework.Archive;
/// <summary>
/// 存档目录
/// </summary>
public class ArchiveCatalogue
{
    public long UnitId; // 单位ID
}
/// <summary>
/// 存档数据
/// </summary>
public class ArchiveData
{
    public long UnitId; // 单位ID
}
//一个简单的示例存储架构
public sealed class ArchiveSystem<T, U> where T : ArchiveCatalogue, new() where U : ArchiveData, new()
{
    public List<T> Catalogues { get; private set; } = new();
    public T CurrentCatalogue { get; private set; }
    public U CurrentData { get; private set; }
    private ArchiveSetting m_Setting;
    public ArchiveSetting Setting
    {
        get
        {
            if (m_Setting == null)
            {
                m_Setting = ResourceLoader.Load<ArchiveSetting>(ResourcesCollectionConstant.Resources_ArchiveSetting);
            }
            return m_Setting;
        }
    }

    /// <summary>
    /// 创建新存档并保存
    /// </summary>
    public async Task SaveAsync()
    {
        var catalogue = new T();
        catalogue.UnitId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Catalogues.Add(catalogue);
        var data = new U();
        data.UnitId = catalogue.UnitId;
        CurrentCatalogue = catalogue;
        CurrentData = data;

        bool catalogueSaved = await EasySave.SaveInUserAsync(Catalogues, $"{Setting.Folder}/Catalogue.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);
        bool dataSaved = await EasySave.SaveInUserAsync(data, $"{Setting.Folder}/Data/{catalogue.UnitId}.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);

        if (catalogueSaved && dataSaved)
        {
            Log.Info("[ArchiveSystem]存档数据成功，单位ID{0}", catalogue.UnitId);
        }
        else
        {
            Log.Error("[ArchiveSystem]存档数据失败，单位ID{0}", catalogue.UnitId);
        }
    }

    /// <summary>
    /// 将当前数据保存到已有存档条目
    /// </summary>
    public async Task SaveAsync(long unitId)
    {
        if (!Catalogues.Exists(x => x.UnitId == unitId))
        {
            Log.Error("[ArchiveSystem]存档目录中不存在该单位ID{0}", unitId);
            return;
        }

        if (CurrentData == null)
        {
            Log.Error("[ArchiveSystem]当前没有数据可保存，单位ID{0}", unitId);
            return;
        }

        CurrentCatalogue = Catalogues.Find(x => x.UnitId == unitId);
        bool saved = await EasySave.SaveInUserAsync(CurrentData, $"{Setting.Folder}/Data/{CurrentCatalogue.UnitId}.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);

        if (saved)
        {
            Log.Info("[ArchiveSystem]保存存档数据成功，单位ID{0}", CurrentCatalogue.UnitId);
        }
        else
        {
            Log.Error("[ArchiveSystem]保存存档数据失败，单位ID{0}", CurrentCatalogue.UnitId);
        }
    }

    /// <summary>
    /// 覆盖当前存档数据
    /// </summary>
    public async Task OverWriteAsync()
    {
        if (CurrentCatalogue == null)
        {
            Log.Error("[ArchiveSystem]当前没有激活的存档，无法覆盖");
            return;
        }
        await SaveAsync(CurrentCatalogue.UnitId);
    }

    /// <summary>
    /// 加载或者初始化存档数据，默认加载最新存档
    /// </summary>
    public async Task LoadAsync()
    {
        var catalogues = await EasySave.LoadFromUserAsync<List<T>>($"{Setting.Folder}/Catalogue.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);
        if (catalogues == null || catalogues.Count == 0)
        {
            await SaveAsync();
        }
        else
        {
            Catalogues = catalogues;
            CurrentCatalogue = Catalogues[^1];
            CurrentData = await EasySave.LoadFromUserAsync<U>($"{Setting.Folder}/Data/{CurrentCatalogue.UnitId}.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);

            if (CurrentData == null)
            {
                Log.Error("[ArchiveSystem]加载存档数据失败，单位ID{0}", CurrentCatalogue.UnitId);
            }
            else
            {
                Log.Info("[ArchiveSystem]加载存档数据成功，单位ID{0}", CurrentCatalogue.UnitId);
            }
        }
    }

    /// <summary>
    /// 按单位ID加载存档数据
    /// </summary>
    public async Task LoadAsync(long unitId)
    {
        if (Catalogues == null || Catalogues.Count == 0)
        {
            Log.Error("[ArchiveSystem]存档目录为空，请先调用 LoadAsync() 初始化");
            return;
        }

        if (!Catalogues.Exists(x => x.UnitId == unitId))
        {
            Log.Error("[ArchiveSystem]存档目录中不存在该单位ID{0}", unitId);
            return;
        }

        CurrentCatalogue = Catalogues.Find(x => x.UnitId == unitId);
        CurrentData = await EasySave.LoadFromUserAsync<U>($"{Setting.Folder}/Data/{CurrentCatalogue.UnitId}.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);

        if (CurrentData == null)
        {
            Log.Error("[ArchiveSystem]存档数据中不存在该单位ID{0}", unitId);
        }
        else
        {
            Log.Info("[ArchiveSystem]加载存档数据成功，单位ID{0}", unitId);
        }
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    public async Task Delete(long unitId)
    {
        if (Catalogues == null || Catalogues.Count == 0)
        {
            Log.Error("[ArchiveSystem]存档目录为空，无法删除");
            return;
        }

        if (!Catalogues.Exists(x => x.UnitId == unitId))
        {
            Log.Error("[ArchiveSystem]存档目录中不存在该单位ID{0}", unitId);
            return;
        }

        Catalogues.Remove(Catalogues.Find(x => x.UnitId == unitId));

        // 如果删除的是当前活跃的存档，重置 CurrentCatalogue 和 CurrentData
        if (CurrentCatalogue != null && CurrentCatalogue.UnitId == unitId)
        {
            CurrentCatalogue = Catalogues.Count > 0 ? Catalogues[^1] : null;
            CurrentData = null;
        }

        await EasySave.DeleteInUserAsync($"{Setting.Folder}/Data/{unitId}.sav");
        await EasySave.SaveInUserAsync(Catalogues, $"{Setting.Folder}/Catalogue.sav", Setting.EnableAesEncryption, Setting.KEY, Setting.Salt);

        Log.Info("[ArchiveSystem]删除存档成功，单位ID{0}", unitId);
    }
}
