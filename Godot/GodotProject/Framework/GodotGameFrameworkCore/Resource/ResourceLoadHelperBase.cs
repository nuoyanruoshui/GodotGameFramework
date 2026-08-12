using System;
using System.Collections;
using GameFramework.Resource;



namespace GodotGameFramework.Resource
{
    /// <summary>
    /// 资源加载辅助器基类。
    /// </summary>
    public abstract partial class ResourceLoadHelperBase : GodotComponent, IResourceLoadHelper
    {
        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        public abstract bool AssetExists(string assetName);

        /// <summary>
        /// 检查文件是否存在。
        /// </summary>
        public abstract bool FileExists(string assetName);

        /// <summary>
        /// 获取二进制文件长度。
        /// </summary>
        public abstract int GetBinaryLength(string assetName);

        /// <summary>
        /// 发起异步资源加载。
        /// </summary>
        public abstract void LoadAssetAsync(string assetName);

        /// <summary>
        /// 轮询资源加载状态与进度。
        /// </summary>
        public abstract LoadResourceStatus GetLoadStatus(string assetName, ICollection progress);
        /// <summary>
        /// 获取加载完成的资源对象。
        /// </summary>
        public abstract object GetAsset(string assetName);

        /// <summary>
        /// 同步读取二进制文件。
        /// </summary>
        public abstract byte[] LoadBinary(string assetName);

        /// <summary>
        /// 异步读取二进制文件（后台线程）。
        /// </summary>
        public abstract void LoadBinaryAsync(string assetName, Action<byte[]> onSuccess, Action<string> onError);



    }
}
