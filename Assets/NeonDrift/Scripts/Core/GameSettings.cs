using UnityEngine;

namespace NeonDrift
{
    /// <summary>玩家设置：存在 PlayerPrefs 里，开游戏时读一次，改了马上生效并保存。</summary>
    public static class GameSettings
    {
        public static string Language = "en";
        public static float Master = 1f, Weapons = 1f, Effects = 1f, Ambience = 1f;   // 0–1
        public static float Shake = 1f;                                                 // 屏幕震动强度 0–1
        public static bool Fullscreen = true, VSync = true;

        public static void Load()
        {
            // 第一次进游戏：系统是中文就默认中文
            string sys = Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseSimplified
                      || Application.systemLanguage == SystemLanguage.ChineseTraditional ? "zh" : "en";
            Language = PlayerPrefs.GetString("nd.lang", sys);
            Master = PlayerPrefs.GetFloat("nd.vol.master", 1f);
            Weapons = PlayerPrefs.GetFloat("nd.vol.weapons", 1f);
            Effects = PlayerPrefs.GetFloat("nd.vol.effects", 1f);
            Ambience = PlayerPrefs.GetFloat("nd.vol.ambience", 1f);
            Shake = PlayerPrefs.GetFloat("nd.shake", 1f);
            Fullscreen = PlayerPrefs.GetInt("nd.fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            VSync = PlayerPrefs.GetInt("nd.vsync", 1) == 1;
            Apply(display: !Application.isEditor);
        }

        public static void Save()
        {
            PlayerPrefs.SetString("nd.lang", Language);
            PlayerPrefs.SetFloat("nd.vol.master", Master);
            PlayerPrefs.SetFloat("nd.vol.weapons", Weapons);
            PlayerPrefs.SetFloat("nd.vol.effects", Effects);
            PlayerPrefs.SetFloat("nd.vol.ambience", Ambience);
            PlayerPrefs.SetFloat("nd.shake", Shake);
            PlayerPrefs.SetInt("nd.fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("nd.vsync", VSync ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>把设置用到游戏上。display：要不要动窗口模式（编辑器里不动）。</summary>
        public static void Apply(bool display = true)
        {
            if (Loc.Lang != Language) Loc.SetLang(Language);
            Sfx.UserMaster = Master; Sfx.UserWeapons = Weapons; Sfx.UserEffects = Effects; Sfx.SetAmbience(Ambience);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (display && Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
        }
    }
}
