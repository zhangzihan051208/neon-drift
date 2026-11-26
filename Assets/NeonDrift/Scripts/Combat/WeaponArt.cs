using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 武器像素图（Tools/gen_weapons_art.py 生成）：StreamingAssets/Weapons/&lt;名字&gt;.png + weapons.json（握持点）。
    /// 文件名 = 武器英文名转小写、非字母数字换成下划线，比如 "Alleyway Pistol" → alleyway_pistol.png。
    /// 用在地上的掉落 / 货架、HUD 武器栏。没有图的武器退回原来的色块图标。
    /// </summary>
    public static class WeaponArt
    {
        public const float PPU = 48f;                 // 比人物（56）略大一点，地上的武器更好认
        class Entry { public Texture2D tex; public Sprite sprite; }
        static Dictionary<string, Entry> cache;

        public static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, Entry>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Weapons");
            if (!Directory.Exists(dir)) return;
            var pivots = new Dictionary<string, Vector2>();
            string jp = Path.Combine(dir, "weapons.json");
            if (File.Exists(jp))
                foreach (Match m in Regex.Matches(File.ReadAllText(jp), "\"([a-z0-9_]+)\":\\s*\\{[^}]*?\"pivotX\":\\s*([0-9.]+),\\s*\"pivotY\":\\s*([0-9.]+)"))
                    pivots[m.Groups[1].Value] = new Vector2(float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                                                            float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(f))) continue;
                string key = Path.GetFileNameWithoutExtension(f);
                var pv = pivots.TryGetValue(key, out var v) ? v : new Vector2(0.3f, 0.3f);
                cache[key] = new Entry { tex = tex, sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pv, PPU, 0, SpriteMeshType.FullRect) };
            }
        }

        public static Sprite GetSprite(WeaponDef w) { Load(); return w != null && cache.TryGetValue(Slug(w.name), out var e) ? e.sprite : null; }

        // ---------------------------------------------------------------- 手持版（Tools/gen_melee_held.py）
        // 近战武器挥砍时拿在零手上的那套图：Weapons/held/<名字>.png + held.json，和人物同一像素密度（PPU 56），按真实比例画，不用再缩放。
        public const float HeldPPU = 56f;
        static Dictionary<string, Sprite> held;
        public static Sprite GetHeldSprite(WeaponDef w)
        {
            if (held == null)
            {
                held = new Dictionary<string, Sprite>();
                string dir = Path.Combine(Application.streamingAssetsPath, "Weapons", "held");
                if (Directory.Exists(dir))
                {
                    var pivots = new Dictionary<string, Vector2>();
                    string jp = Path.Combine(dir, "held.json");
                    if (File.Exists(jp))
                        foreach (Match m in Regex.Matches(File.ReadAllText(jp), "\"([a-z0-9_]+)\":\\s*\\{[^}]*?\"pivotX\":\\s*([0-9.]+),\\s*\"pivotY\":\\s*([0-9.]+)"))
                            pivots[m.Groups[1].Value] = new Vector2(float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                                                                    float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
                    foreach (var f in Directory.GetFiles(dir, "*.png"))
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                        if (!tex.LoadImage(File.ReadAllBytes(f))) continue;
                        string key = Path.GetFileNameWithoutExtension(f);
                        var pv = pivots.TryGetValue(key, out var v) ? v : new Vector2(0.15f, 0.5f);
                        held[key] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pv, HeldPPU, 0, SpriteMeshType.FullRect);
                    }
                }
            }
            return w != null && held.TryGetValue(Slug(w.name), out var sp) ? sp : null;
        }
        public static Texture2D Tex(WeaponDef w) { Load(); return w != null && cache.TryGetValue(Slug(w.name), out var e) ? e.tex : null; }

        /// <summary>在 HUD 上画武器图：按高度缩放、保持像素清晰，返回画出来的宽度。</summary>
        public static float DrawGUI(WeaponDef w, Rect area)
        {
            var t = Tex(w);
            if (t == null) return 0f;
            float k = Mathf.Max(1f, Mathf.Floor(Mathf.Min(area.height / t.height, area.width / t.width)));
            var r = new Rect(area.x, area.yMax - t.height * k, t.width * k, t.height * k);
            GUI.DrawTexture(r, t);
            return r.width;
        }
    }
}
