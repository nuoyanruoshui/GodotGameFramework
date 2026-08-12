using System;
using System.Collections;
using Godot;
using GodotGameFramework.Resource;

namespace GameFramework.Resource
{
    /// <summary>
    /// 默认资源加载辅助器，基于 Godot ResourceLoader 与 System.IO。
    /// </summary>
    public partial class DefaultResourceLoadHelper : ResourceLoadHelperBase
    {
        public override bool AssetExists(string assetName)
        {
            return ResourceLoader.Exists(assetName);
        }

        public override bool FileExists(string assetName)
        {
            return FileAccess.FileExists(assetName);
        }

        public override int GetBinaryLength(string assetName)
        {
            if (!FileAccess.FileExists(assetName)) return -1;
            using var file = FileAccess.Open(assetName, FileAccess.ModeFlags.Read);
            return file != null ? (int)file.GetLength() : -1;
        }

        public override void LoadAssetAsync(string assetName)
        {
            ResourceLoader.LoadThreadedRequest(assetName);
        }

        public override object GetAsset(string assetName)
        {
            return ResourceLoader.LoadThreadedGet(assetName);
        }

        public override byte[] LoadBinary(string assetName)
        {
            using FileAccess file = FileAccess.Open(assetName, FileAccess.ModeFlags.Read);
            return file != null ? file.GetBuffer((long)file.GetLength()) : null;
        }

        public override void LoadBinaryAsync(string assetName, Action<byte[]> onSuccess, Action<string> onError)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (System.IO.File.Exists(assetName))
                    {
                        onSuccess(System.IO.File.ReadAllBytes(assetName));
                    }
                    else
                    {
                        onError(Utility.Text.Format("File '{0}' does not exist.", assetName));
                    }
                }
                catch (Exception ex)
                {
                    onError(ex.Message);
                }
            });
        }

        public override LoadResourceStatus GetLoadStatus(string assetName, ICollection progress)
        {
            LoadResourceStatus status = LoadResourceStatus.Success;
            ResourceLoader.ThreadLoadStatus state = ResourceLoader.LoadThreadedGetStatus(assetName, (Godot.Collections.Array)progress);
            switch (state)
            {
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                case ResourceLoader.ThreadLoadStatus.Failed:
                    status = LoadResourceStatus.AssetError;
                    break;
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    status = LoadResourceStatus.InProgress;
                    break;
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    status = LoadResourceStatus.Success;
                    break;
            }
            return status;
        }

    }
}
