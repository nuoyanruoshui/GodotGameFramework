//------------------------------------------------------------
// SoundExtension - 音频组件便捷扩展方法
//------------------------------------------------------------

using GameFramework.Sound;
using GodotGameFramework.Scene;

namespace GodotGameFramework.Sound
{
    /// <summary>
    /// 音频组件扩展方法。
    /// </summary>
    public static class SoundExtension
    {


        /// <summary>
        /// 播放背景音乐（BGM）。
        ///
        /// 使用 Music 组播放，优先级为 0（默认）。
        /// Music 组默认配置为 2 个 Agent 且避免被同优先级替换。
        ///
        /// <code>
        /// GF.Sound.PlayBGM("res://Audio/background.mp3");
        /// </code>
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径（res:// 协议）。</param>
        /// <returns>声音序列号，可用于后续停止/暂停操作。</returns>
        public static int PlayBGM(this SoundComponent soundComponent, string soundAssetName)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(SoundComponent.DefaultMusicGroup);
            group.StopAllLoadedSounds();
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultMusicGroup);
        }

        /// <summary>
        /// 播放背景音乐（BGM），带用户自定义数据。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音序列号。</returns>
        public static int PlayBGM(this SoundComponent soundComponent, string soundAssetName, object userData)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(SoundComponent.DefaultMusicGroup);
            group.StopAllLoadedSounds();
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultMusicGroup, 0, userData);
        }

        /// <summary>
        /// 播放音效（SFX）。
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径。</param>
        /// <returns>声音序列号。</returns>
        public static int PlaySFX(this SoundComponent soundComponent, string soundAssetName)
        {
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultSfxGroup);
        }

        /// <summary>
        /// 播放音效（SFX），带用户自定义数据。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音序列号。</returns>
        public static int PlaySFX(this SoundComponent soundComponent, string soundAssetName, object userData)
        {
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultSfxGroup, 0, userData);
        }

        /// <summary>
        /// 播放 UI 音效。
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径。</param>
        /// <returns>声音序列号。</returns>
        public static int PlayUISound(this SoundComponent soundComponent, string soundAssetName)
        {
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultUiGroup);
        }

        /// <summary>
        /// 播放 UI 音效，带用户自定义数据。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="soundAssetName">音频资源路径。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音序列号。</returns>
        public static int PlayUISound(this SoundComponent soundComponent, string soundAssetName, object userData)
        {
            return soundComponent.PlaySound(soundAssetName, SoundComponent.DefaultUiGroup, 0, userData);
        }

        /// <summary>
        /// 停止所有背景音乐（仅 Music 组）。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        public static void StopBGM(this SoundComponent soundComponent)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(SoundComponent.DefaultMusicGroup);
            group?.StopAllLoadedSounds();
        }

        /// <summary>
        /// 淡出停止所有背景音乐（仅 Music 组）。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        /// <param name="fadeOutSeconds">淡出时长（秒）。</param>
        public static void StopBGM(this SoundComponent soundComponent, float fadeOutSeconds)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(SoundComponent.DefaultMusicGroup);
            group?.StopAllLoadedSounds(fadeOutSeconds);
        }

        /// <summary>
        /// 停止所有音效（仅 SFX 组）。
        /// </summary>
        /// <param name="soundComponent">音频组件。</param>
        public static void StopSFX(this SoundComponent soundComponent)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(SoundComponent.DefaultSfxGroup);
            group?.StopAllLoadedSounds();
        }
        /// <summary>
        /// 设置音量。
        /// </summary>
        /// <param name="soundComponent"></param>
        /// <param name="bus"></param>
        /// <param name="volume">0~1的音量值</param>
        public static void SetVolume(this SoundComponent soundComponent, string bus, float volume)
        {
            ISoundGroup group = soundComponent.GetSoundGroup(bus);
            if (group != null)
            {
                group.Volume = volume;
                GF.Setting.SetFloat(bus, volume);
                GF.Setting.Save();
            }
            else
            {
                Log.Error($"SoundComponent: SetVolume({bus}, {volume}) failed, group is null.");
            }
        }
    }
}
