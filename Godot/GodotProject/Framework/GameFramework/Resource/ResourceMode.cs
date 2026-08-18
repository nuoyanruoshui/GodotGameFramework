//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Resource
{
    public enum ResourceMode : byte
    {
        /// <summary>
        /// 单机模式。
        /// </summary>
        Package = 1,

        /// <summary>
        /// 热更模式，自定义热更包
        /// </summary>
        Updatable = 2,
    }
}
