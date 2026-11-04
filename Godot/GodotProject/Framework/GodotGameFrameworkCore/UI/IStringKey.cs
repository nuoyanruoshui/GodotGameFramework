namespace GodotGameFramework.Localization
{
    /// <summary>
    /// 本地化接口
    /// </summary>
    public interface IStringKey
    {
        string StringKey { get; }
        void SetLocalizationValue();
    }
}