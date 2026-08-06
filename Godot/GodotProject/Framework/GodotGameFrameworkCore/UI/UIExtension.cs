using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameConfig;
using GameConfig.Constant;
using GameConfig.UI;
using GameFramework.UI;
using GameLogic;

namespace GodotGameFramework.UI
{
    public static class UIExtension
    {
        public static bool HasUIForm<T>(this UIComponent uiComponent, string uiFormAssetName) where T : IUIForm
        {
            IUIForm[] uiForms = uiComponent.GetUIForms(uiFormAssetName);
            return uiForms.Any(form => form is T);
        }

        public static void CloseUIForm(this UIComponent uiComponent, string uiFormAssetName, object userData = null)
        {
            IUIForm uiForm = uiComponent.GetUIForm(uiFormAssetName);
            if (uiForm != null)
            {
                uiComponent.CloseUIForm(uiForm, userData);
            }
        }

        public static void CloseUIForms(this UIComponent uiComponent, string uiGroupName, object userData = null)
        {
            IUIForm[] uiForms = uiComponent.GetAllLoadedUIForms();
            for (int i = 0; i < uiForms.Length; i++)
            {
                if (uiForms[i] != null && uiComponent.IsValidUIForm(uiForms[i]))
                {
                    if (uiForms[i].UIGroup?.Name == uiGroupName)
                    {
                        uiComponent.CloseUIForm(uiForms[i], userData);
                    }
                }
            }
        }

        public static IUIForm GetTopUIForm(this UIComponent uiComponent)
        {
            IUIForm[] uiForms = uiComponent.GetAllLoadedUIForms();
            if (uiForms.Length == 0) return null;

            // 所有已加载的 UI 都已被 Refresh 算法排序，
            // 最后一个即为最顶层（深度最大）
            return uiForms[uiForms.Length - 1];
        }

        public static int OpenUIForm(this UIComponent uiComponent, string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData)
        {
            return uiComponent.OpenUIForm(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, userData);
        }
        public static int OpenUIForm(this UIComponent uiComponent, string uiFormAssetName, string uiGroupName, object userData = null)
        {
            return uiComponent.OpenUIForm(uiFormAssetName, uiGroupName, userData);
        }
        public static int OpenUIForm(this UIComponent uiComponent, UIFormId formId, object userData = null)
        {
            if (ConfigSystem.Instance.Tables.TbUIFormConfig?.DataList == null)
            {
                throw new Exception("UIFormConfig data table is not available.");
            }
            UIFormConfig formConfig = ConfigSystem.Instance.Tables.TbUIFormConfig.DataList.FirstOrDefault(x => x.UIFormId == formId);
            if (formConfig == null)
            {
                throw new Exception($"找不到UIFormId:{formId}的配置");
            }
            return uiComponent.OpenUIForm(formConfig.AssetPath, formConfig.UIGroupName, userData);
        }
        /// <summary>
        /// 异步打开UI表单
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="uiComponent"></param>
        /// <param name="formId"></param>
        /// <param name="userData"></param>
        /// <returns></returns>
        public static async Task<T> OpenUIFormAsync<T>(this UIComponent uiComponent, UIFormId formId, object userData = null) where T : class, IUIForm
        {
            if (ConfigSystem.Instance.Tables.TbUIFormConfig?.DataList == null)
            {
                throw new Exception("UIFormConfig data table is not available.");
            }
            UIFormConfig formConfig = ConfigSystem.Instance.Tables.TbUIFormConfig.DataList.FirstOrDefault(x => x.UIFormId == formId);
            if (formConfig == null)
            {
                throw new Exception($"找不到UIFormId:{formId}的配置");
            }
            var uIForm = await uiComponent.OpenUIFormAsync(formConfig.AssetPath, formConfig.UIGroupName, formConfig.PauseCoveredUIForm, userData);
            return uIForm as T;
        }

        const string MainPack = "MainPack";
        public static async Task<LoadingForm> OpenLoadingUIFormAsync(this UIComponent uiComponent)
        {
            if (GF.UI.AddUIGroup(MainPack))
            {
                return (LoadingForm)await GF.UI.OpenUIFormAsync(ResourcesCollectionConstant.UI_LoadingForm, MainPack);
            }
            else
            {
                return (LoadingForm)await GF.UI.OpenUIFormAsync(ResourcesCollectionConstant.UI_LoadingForm, MainPack);
            }
        }
        public static bool HasUIForm(this UIComponent uiComponent, UIFormId formId)
        {
            UIFormConfig formConfig = ConfigSystem.Instance.Tables.TbUIFormConfig.DataList.FirstOrDefault(x => x.UIFormId == formId);
            IUIForm[] uiForms = uiComponent.GetUIForms(formConfig.AssetPath);
            return uiForms.Length > 0;
        }
    }
}
