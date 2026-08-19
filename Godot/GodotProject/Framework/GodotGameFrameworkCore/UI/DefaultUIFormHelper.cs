//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.UI;
using Godot;
using System;

namespace GodotGameFramework.UI
{
    /// <summary>
    /// 默认界面辅助器。
    /// </summary>
    public partial class DefaultUIFormHelper : UIFormHelperBase, IUIFormHelper
    {
        /// <summary>
        /// 实例化界面。
        /// <returns>实例化后的界面（Node）。</returns>
        public override object InstantiateUIForm(object uiFormAsset)
        {
            return NodeUtility.InstantiatePack(uiFormAsset);
        }

        /// <summary>
        /// 创建界面。
        /// </summary>
        /// <param name="uiFormInstance">界面实例（从 PackedScene 实例化的 Node）。</param>
        /// <param name="uiGroup">界面所属的界面组。</param>
        /// <param name="userData">用户自定义数据（包含 UIFormLogic 类型信息）。</param>
        /// <returns>创建的界面。</returns>
        public override IUIForm CreateUIForm(object uiFormInstance, IUIGroup uiGroup, object userData)
        {
            // 创建 UIForm 包装器节点
            if (uiFormInstance is IUIForm uIForm)
            {
                var uiForm = (CanvasItem)uIForm;
                Node groupContainer = ((Node)uiGroup.Helper);
                if (groupContainer == null)
                {
                    Log.Error("UI group helper is invalid.");
                    return null;
                }

                if (uiForm.GetParent() != groupContainer)
                {
                    uiForm.Name = "UIForm_" + uiForm.GetType().Name;
                    groupContainer.AddChild(uiForm);
                }
                uiForm.MoveToFront();
                return uIForm;
            }
            else
            {
                Log.Error("{0} is not UIFormLogic type.", uiFormInstance);
                return null;
            }
        }

        /// <summary>
        /// 释放界面。
        ///
        /// 销毁 UIForm 节点及其所有子节点。
        /// </summary>
        /// <param name="uiFormAsset">要释放的界面资源。</param>
        /// <param name="uiFormInstance">要释放的界面实例。</param>
        public override void ReleaseUIForm(object uiFormAsset, object uiFormInstance)
        {
            NodeUtility.ReleaseNode(uiFormInstance);
        }
    }
}
