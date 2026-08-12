using System;
using System.Collections;


namespace GameFramework.Resource
{
    /// <summary>
    /// 资源加载辅助器接口。抽象资源与二进制的加载操作，便于替换加载实现。
    /// </summary>
    public interface IResourceLoadHelper
    {
        /// <summary>
        /// 检查资源是否存在（Godot ResourceLoader）。
        /// </summary>
        bool AssetExists(string assetName);

        /// <summary>
        /// 检查文件是否存在。
        /// </summary>
        bool FileExists(string assetName);

        /// <summary>
        /// 获取二进制文件长度。
        /// </summary>
        int GetBinaryLength(string assetName);

        /// <summary>
        /// 发起异步资源加载。
        /// </summary>
        void LoadAssetAsync(string assetName);

        /// <summary>
        /// 轮询资源加载状态与进度。
        /// </summary>
        LoadResourceStatus GetLoadStatus(string assetName, ICollection progress);

        /// <summary>
        /// 获取加载完成的资源对象。
        /// </summary>
        object GetAsset(string assetName);

        /// <summary>
        /// 同步读取二进制文件。
        /// </summary>
        byte[] LoadBinary(string assetName);

        /// <summary>
        /// 异步读取二进制文件（后台线程）。
        /// </summary>
        void LoadBinaryAsync(string assetName, Action<byte[]> onSuccess, Action<string> onError);
    }
}
