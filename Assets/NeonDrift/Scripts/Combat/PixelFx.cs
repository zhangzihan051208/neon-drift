using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 像素风格的战斗特效，全部用代码逐像素画（硬边、点采样、32 像素/单位，和场景同一个像素尺度）。
    /// 图都是白色和灰色，亮度就是强度；播放时用发光材质染成武器的颜色，所以同一个形状可以给不同武器用。
    /// 子弹：pistol 手枪 / rifle 步枪 / pellet 霰弹 / sniper 狙击 / mg 机枪 / grenade 榴弹 / flame 火焰 / bolt 电弧
    /// 动画：Muzzle 枪口火光、Impact 命中、Explosion 爆炸、Slash 刀光、Smash 砸地冲击波
    /// </summary>
    public static class PixelFx
    {
        const int PPU = Sprites.PPU;
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

        // ---------------------------------------------------------------- 画布
        class Canvas
        {
            public readonly int w, h; public readonly Color32[] px;
            public Canvas(int w, int h) { this.w = w; this.h = h; px = new Color32[w * h]; }
            public void Set(int x, int y, float v)
            {
                if (x < 0 || y < 0 || x >= w || y >= h || v <= 0f) return;
                byte b = (byte)(Mathf.Clamp01(v) * 255f);
                var o = px[y * w + x]; if (o.a >= b) return;
                byte g = (byte)(v > 0.8f ? 255 : 210);                         // 最亮的地方是纯白芯，其余偏灰
                px[y * w + x] = new Color32(g, g, g, b);
            }
            public Sprite ToSprite(string name, Vector2 pivot)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
                tex.SetPixels32(px); tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PPU, 0, SpriteMeshType.FullRect);
            }
        }

        static Sprite[] Cached(string key, System.Func<Sprite[]> make)
        {
            if (!cache.TryGetValue(key, out var s) || s == null || s.Length == 0 || s[0] == null) cache[key] = s = make();
            return s;
        }

        // ---------------------------------------------------------------- 子弹
        /// <summary>子弹的帧（大多只有 1 帧；火焰、电弧有几帧，飞行时循环）。朝右画，支点在弹头稍后。</summary>
        public static Sprite[] Bullet(string kind) => Cached("b_" + kind, () =>
        {
            switch (kind)
            {
                case "rifle": return new[] { Line(9, 2, 0.55f, 1f, true, "rifle") };
                case "sniper": return new[] { Line(18, 3, 0.45f, 1f, true, "sniper") };
                case "mg": return new[] { Line(7, 2, 0.5f, 1f, true, "mg") };
                case "pellet":
                {
                    var c = new Canvas(3, 3); c.Set(1, 1, 1f); c.Set(0, 1, 0.6f); c.Set(2, 1, 0.8f); c.Set(1, 0, 0.5f); c.Set(1, 2, 0.5f);
                    return new[] { c.ToSprite("pellet", new Vector2(0.5f, 0.5f)) };
                }
                case "grenade":
                {
                    var c = new Canvas(5, 5);
                    for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
                    {
                        float d = new Vector2(x - 2, y - 2).magnitude;
                        if (d <= 2.3f) c.Set(x, y, d < 1f ? 1f : 0.55f);
                    }
                    c.Set(3, 3, 1f);                                                // 高光
                    return new[] { c.ToSprite("grenade", new Vector2(0.5f, 0.5f)) };
                }
                case "flame":
                {
                    var fr = new Sprite[4];
                    var rng = new System.Random(7);
                    for (int f = 0; f < 4; f++)
                    {
                        int s = 5 + f * 2; var c = new Canvas(s, s); float r = s / 2f;
                        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                        {
                            float d = new Vector2(x - r + 0.5f, y - r + 0.5f).magnitude / r;
                            if (d > 1f) continue;
                            if (f >= 2 && ((x + y + f) % 2 == 0) && d > 0.5f) continue;      // 越往后越稀，抖动出像素颗粒
                            c.Set(x, y, d < 0.35f && f < 2 ? 1f : 0.7f - d * 0.3f + (float)rng.NextDouble() * 0.1f);
                        }
                        fr[f] = c.ToSprite("flame" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "bolt":
                {
                    var fr = new Sprite[3];
                    var rng = new System.Random(3);
                    for (int f = 0; f < 3; f++)
                    {
                        var c = new Canvas(12, 7); int y = 3;
                        for (int x = 0; x < 12; x++)
                        {
                            c.Set(x, y, 1f);
                            if (x % 2 == 1) { int ny = Mathf.Clamp(y + rng.Next(-2, 3), 1, 5); for (int yy = Mathf.Min(y, ny); yy <= Mathf.Max(y, ny); yy++) c.Set(x, yy, 0.85f); y = ny; }
                        }
                        c.Set(11, 3, 1f); c.Set(10, 2, 0.6f); c.Set(10, 4, 0.6f);
                        fr[f] = c.ToSprite("bolt" + f, new Vector2(0.75f, 0.5f));
                    }
                    return fr;
                }
                case "smg": return new[] { Line(5, 2, 0.6f, 1f, true, "smg") };
                case "shuriken":                                                    // 手里剑：四角星，两帧转 45°
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(9, 9);
                        c.Set(4, 4, 0.5f);
                        for (int k = 1; k <= 4; k++)
                        {
                            float v = 1f - k * 0.1f;
                            if (f == 0) { c.Set(4 + k, 4, v); c.Set(4 - k, 4, v); c.Set(4, 4 + k, v); c.Set(4, 4 - k, v); }
                            else if (k <= 3) { c.Set(4 + k, 4 + k, v); c.Set(4 - k, 4 - k, v); c.Set(4 + k, 4 - k, v); c.Set(4 - k, 4 + k, v); }
                        }
                        if (f == 0) { c.Set(5, 5, 0.6f); c.Set(3, 3, 0.6f); c.Set(5, 3, 0.6f); c.Set(3, 5, 0.6f); }
                        else { c.Set(5, 4, 0.6f); c.Set(3, 4, 0.6f); c.Set(4, 5, 0.6f); c.Set(4, 3, 0.6f); }
                        fr[f] = c.ToSprite("shuriken" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "knife":                                                       // 飞刀：刀身 + 深色刀柄
                {
                    var c = new Canvas(9, 3);
                    for (int x = 3; x < 9; x++) c.Set(x, 1, x == 8 ? 1f : 0.85f);
                    for (int x = 4; x < 8; x++) c.Set(x, 2, 0.55f);
                    for (int x = 0; x < 3; x++) c.Set(x, 1, 0.35f);
                    return new[] { c.ToSprite("knife", new Vector2(0.6f, 0.5f)) };
                }
                case "javelin":                                                     // 标枪：长杆 + 尖头
                {
                    var c = new Canvas(22, 5);
                    for (int x = 0; x < 17; x++) c.Set(x, 2, 0.5f + x * 0.01f);
                    for (int x = 17; x < 22; x++) { c.Set(x, 2, 1f); if (x < 20) { c.Set(x, 1, 0.75f); c.Set(x, 3, 0.75f); } }
                    c.Set(0, 1, 0.45f); c.Set(0, 3, 0.45f);
                    return new[] { c.ToSprite("javelin", new Vector2(0.8f, 0.5f)) };
                }
                case "chakram":                                                     // 飞轮：一圈环带四个刃尖，转起来
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(11, 11);
                        for (int a = 0; a < 360; a += 15)
                        {
                            float r = a * Mathf.Deg2Rad;
                            c.Set(5 + Mathf.RoundToInt(Mathf.Cos(r) * 3.5f), 5 + Mathf.RoundToInt(Mathf.Sin(r) * 3.5f), 0.85f);
                        }
                        for (int k = 0; k < 4; k++)
                        {
                            float r = (k * 90 + (f == 0 ? 20 : 65)) * Mathf.Deg2Rad;
                            c.Set(5 + Mathf.RoundToInt(Mathf.Cos(r) * 5f), 5 + Mathf.RoundToInt(Mathf.Sin(r) * 5f), 1f);
                        }
                        fr[f] = c.ToSprite("chakram" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "caltrop":                                                     // 铁蒺藜：一小团带刺的铁
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(5, 5);
                        c.Set(2, 2, 0.8f);
                        if (f == 0) { c.Set(0, 0, 0.7f); c.Set(4, 0, 0.7f); c.Set(2, 4, 0.7f); c.Set(1, 1, 0.6f); c.Set(3, 1, 0.6f); c.Set(2, 3, 0.6f); }
                        else { c.Set(0, 4, 0.7f); c.Set(4, 4, 0.7f); c.Set(2, 0, 0.7f); c.Set(1, 3, 0.6f); c.Set(3, 3, 0.6f); c.Set(2, 1, 0.6f); }
                        fr[f] = c.ToSprite("caltrop" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "needle": return new[] { Line(6, 1, 0.5f, 1f, false, "needle") };
                case "slug":                                                        // 左轮：粗短的大口径弹头
                {
                    var c = new Canvas(8, 4);
                    for (int x = 0; x < 8; x++)
                    {
                        float v = x >= 5 ? 1f : 0.45f + x * 0.1f;
                        c.Set(x, 1, v); c.Set(x, 2, v);
                        if (x >= 2 && x <= 6) { c.Set(x, 0, v * 0.5f); c.Set(x, 3, v * 0.5f); }
                    }
                    return new[] { c.ToSprite("slug", new Vector2(0.75f, 0.5f)) };
                }
                case "smart":                                                       // 智能手枪：菱形弹芯，外面一圈锁定点在闪
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(9, 9);
                        for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++)
                            if (Mathf.Abs(x) + Mathf.Abs(y) <= 2) c.Set(4 + x, 4 + y, Mathf.Abs(x) + Mathf.Abs(y) == 0 ? 1f : 0.7f);
                        int r = f == 0 ? 4 : 3;
                        c.Set(4 + r, 4 + r, 0.6f); c.Set(4 - r, 4 + r, 0.6f); c.Set(4 + r, 4 - r, 0.6f); c.Set(4 - r, 4 - r, 0.6f);
                        fr[f] = c.ToSprite("smart" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "stinger":                                                     // 黄蜂：尖刺弹，两侧小翅膀扇动
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(8, 5);
                        for (int x = 0; x < 8; x++) c.Set(x, 2, 0.5f + x * 0.07f);
                        for (int x = 2; x < 7; x++) { c.Set(x, 1, 0.5f); c.Set(x, 3, 0.5f); }
                        c.Set(7, 2, 1f);
                        int wx = f == 0 ? 3 : 4;
                        c.Set(wx, 0, 0.8f); c.Set(wx, 4, 0.8f); c.Set(wx - 1, f == 0 ? 0 : 4, 0.5f);
                        fr[f] = c.ToSprite("stinger" + f, new Vector2(0.75f, 0.5f));
                    }
                    return fr;
                }
                case "nail":                                                        // 钉枪：钉身 + 钉帽
                {
                    var c = new Canvas(9, 3);
                    for (int x = 1; x < 8; x++) c.Set(x, 1, 0.75f);
                    c.Set(8, 1, 1f); c.Set(0, 0, 0.9f); c.Set(0, 1, 0.9f); c.Set(0, 2, 0.9f);
                    return new[] { c.ToSprite("nail", new Vector2(0.75f, 0.5f)) };
                }
                case "rail":                                                        // 轨道炮：又长又亮的弹芯
                {
                    var c = new Canvas(28, 5);
                    for (int x = 0; x < 28; x++)
                    {
                        float u = x / 27f;
                        c.Set(x, 2, Mathf.Lerp(0.6f, 1f, u)); c.Set(x, 1, Mathf.Lerp(0.4f, 0.95f, u)); c.Set(x, 3, Mathf.Lerp(0.4f, 0.95f, u));
                        if (x > 14) { c.Set(x, 0, 0.45f); c.Set(x, 4, 0.45f); }
                    }
                    return new[] { c.ToSprite("rail", new Vector2(0.8f, 0.5f)) };
                }
                case "missile":                                                     // 微型导弹：弹体、尾翼、闪烁的尾焰
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(10, 5);
                        for (int x = 3; x < 9; x++) for (int y = 1; y <= 3; y++) c.Set(x, y, y == 2 ? 0.8f : 0.6f);
                        c.Set(9, 2, 1f);
                        c.Set(3, 0, 0.6f); c.Set(3, 4, 0.6f); c.Set(4, 0, 0.5f); c.Set(4, 4, 0.5f);
                        c.Set(2, 2, 1f); c.Set(1, 2, f == 0 ? 1f : 0.7f); if (f == 0) c.Set(0, 2, 0.8f);
                        fr[f] = c.ToSprite("missile" + f, new Vector2(0.7f, 0.5f));
                    }
                    return fr;
                }
                case "orb":                                                         // 电炮：一团噼啪作响的电球
                {
                    var fr = new Sprite[3];
                    var rng = new System.Random(11);
                    for (int f = 0; f < 3; f++)
                    {
                        var c = new Canvas(13, 13); int m = 6;
                        for (int y = 0; y < 13; y++) for (int x = 0; x < 13; x++)
                        {
                            float d = new Vector2(x - m, y - m).magnitude;
                            if (d <= 2.2f) c.Set(x, y, 1f); else if (d <= 3.2f && (x + y + f) % 2 == 0) c.Set(x, y, 0.65f);
                        }
                        for (int k = 0; k < 4; k++)                                    // 往外乱窜的电丝
                        {
                            float a = (float)rng.NextDouble() * Mathf.PI * 2f; float px = m, py = m;
                            for (int st = 0; st < 6; st++)
                            {
                                a += (float)(rng.NextDouble() - 0.5) * 1.2f; px += Mathf.Cos(a); py += Mathf.Sin(a);
                                c.Set(Mathf.RoundToInt(px), Mathf.RoundToInt(py), 0.85f - st * 0.08f);
                            }
                        }
                        fr[f] = c.ToSprite("orb" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                case "ember":                                                       // 龙息：一颗颗燃烧的弹丸
                {
                    var fr = new Sprite[2];
                    for (int f = 0; f < 2; f++)
                    {
                        var c = new Canvas(5, 5);
                        c.Set(2, 2, 1f); c.Set(1, 2, 0.8f); c.Set(3, 2, 0.9f); c.Set(2, 1, 0.7f); c.Set(2, 3, 0.7f);
                        if (f == 0) { c.Set(0, 2, 0.5f); c.Set(1, 3, 0.5f); } else { c.Set(1, 1, 0.5f); c.Set(0, 3, 0.4f); }
                        fr[f] = c.ToSprite("ember" + f, new Vector2(0.5f, 0.5f));
                    }
                    return fr;
                }
                default: return new[] { Line(6, 3, 0.6f, 1f, false, "pistol") };
            }
        });

        /// <summary>细长的曳光弹：中间一行亮芯从尾到头由暗变亮，上下各一行外圈（rim=false 时外圈只在中段）。</summary>
        static Sprite Line(int len, int thick, float tail, float head, bool fullRim, string name)
        {
            var c = new Canvas(len, thick); int mid = thick / 2;
            for (int x = 0; x < len; x++)
            {
                float u = len == 1 ? 1f : x / (float)(len - 1);
                c.Set(x, mid, Mathf.Lerp(tail, head, u));
                bool rim = fullRim ? x >= len / 3 : x >= 1 && x <= len - 2;
                if (rim) for (int y = 0; y < thick; y++) if (y != mid) c.Set(x, y, Mathf.Lerp(tail, head, u) * 0.5f);
            }
            return c.ToSprite(name, new Vector2(0.75f, 0.5f));
        }

        // ---------------------------------------------------------------- 动画特效
        /// <summary>枪口火光：朝右喷出的一小簇，3 帧由大变小。size 是长度（像素）。</summary>
        public static Sprite[] Muzzle(int size) => Cached("m_" + size, () =>
        {
            var fr = new Sprite[3];
            for (int f = 0; f < 3; f++)
            {
                int L = Mathf.Max(2, Mathf.RoundToInt(size * (1f - f * 0.3f))); int H = L + 2;
                var c = new Canvas(L + 2, H); int my = H / 2;
                for (int x = 0; x <= L; x++)
                {
                    int half = Mathf.RoundToInt((L - x) * 0.45f * (f == 2 ? 0.5f : 1f));
                    for (int y = -half; y <= half; y++) c.Set(x, my + y, x < 2 && Mathf.Abs(y) <= 1 ? 1f : 0.85f - Mathf.Abs(y) * 0.12f - f * 0.2f);
                }
                if (f == 0) { c.Set(1, my + Mathf.Max(2, L / 3), 0.8f); c.Set(1, my - Mathf.Max(2, L / 3), 0.8f); }   // 两侧散出的火星
                fr[f] = c.ToSprite("muzzle" + f, new Vector2(0f, (my + 0.5f) / H));
            }
            return fr;
        });

        /// <summary>命中：亮芯加四道短刺 → 点状小环 → 更大更稀的环。</summary>
        public static Sprite[] Impact(int r) => Cached("i_" + r, () =>
        {
            int s = r * 2 + 3; var fr = new Sprite[3]; int m = s / 2;
            var c0 = new Canvas(s, s);
            for (int k = -1; k <= 1; k++) for (int j = -1; j <= 1; j++) c0.Set(m + k, m + j, 1f);
            for (int i = 2; i <= r; i++) { float v = 1f - i / (float)(r + 1); c0.Set(m + i, m, v); c0.Set(m - i, m, v); c0.Set(m, m + i, v); c0.Set(m, m - i, v); }
            fr[0] = c0.ToSprite("imp0", new Vector2(0.5f, 0.5f));
            for (int f = 1; f < 3; f++)
            {
                var c = new Canvas(s, s); float rr = r * (f == 1 ? 0.6f : 1f);
                for (int a = 0; a < 360; a += f == 1 ? 30 : 45)
                {
                    float rad = a * Mathf.Deg2Rad;
                    c.Set(m + Mathf.RoundToInt(Mathf.Cos(rad) * rr), m + Mathf.RoundToInt(Mathf.Sin(rad) * rr), f == 1 ? 0.9f : 0.5f);
                }
                if (f == 1) c.Set(m, m, 0.7f);
                fr[f] = c.ToSprite("imp" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>爆炸：白色实心球 → 带亮边的火球 → 抖动网点的火团 → 粗环 → 稀疏的环 → 零星火星。斜视角下压扁一点。</summary>
        public static Sprite[] Explosion(int r) => Cached("e_" + r, () =>
        {
            int s = r * 2 + 5, m = s / 2; var fr = new Sprite[6];
            var rng = new System.Random(r);
            for (int f = 0; f < 6; f++)
            {
                var c = new Canvas(s, s);
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = x - m, dy = (y - m) / 0.8f, d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    switch (f)
                    {
                        case 0: if (d <= 0.45f) c.Set(x, y, 1f); break;
                        case 1: if (d <= 0.75f) c.Set(x, y, d > 0.6f ? 1f : 0.85f); break;
                        case 2: if (d <= 1f && ((x + y) % 2 == 0 || d > 0.8f)) c.Set(x, y, d > 0.8f ? 0.95f : 0.7f); break;
                        case 3: if (d > 0.8f && d <= 1.05f) c.Set(x, y, 0.8f); else if (d <= 0.8f && (x + 2 * y) % 5 == 0) c.Set(x, y, 0.45f); break;
                        case 4: if (d > 0.95f && d <= 1.1f && (x + y) % 2 == 0) c.Set(x, y, 0.6f); break;
                        case 5: if (d <= 1.15f && rng.NextDouble() < 0.025) c.Set(x, y, 0.7f); break;
                    }
                }
                fr[f] = c.ToSprite("exp" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>
        /// 刀光：以角色为圆心、朝右的一道月牙，arc 是张开的角度。
        /// 第 1 帧只画出前一半，第 2 帧整道最亮，第 3 帧变细，第 4 帧碎成网点。外缘最亮，往里渐暗，两头收尖。
        /// </summary>
        public static Sprite[] Slash(int radius, float arc) => Cached("s_" + radius + "_" + arc, () =>
        {
            int s = radius * 2 + 4, m = s / 2; var fr = new Sprite[4];
            float half = arc * 0.5f;
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(s, s);
                float thick = f == 2 ? radius * 0.18f : f == 3 ? radius * 0.14f : radius * 0.32f;
                float a0 = -half, a1 = f == 0 ? -half + arc * 0.55f : half;
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = x - m + 0.5f, dy = y - m + 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (ang < a0 || ang > a1) continue;
                    float u = (ang - a0) / Mathf.Max(1f, half * 2f);                  // 0 → 1：从起刀到收刀
                    float taper = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);               // 两头收尖
                    float t = thick * (0.25f + 0.75f * taper);
                    if (d > radius || d < radius - t) continue;
                    float edge = 1f - (radius - d) / Mathf.Max(1f, t);                  // 外缘 1，里面 0
                    if (f == 3 && (x + y) % 2 == 1) continue;
                    float v = f == 1 ? 0.55f + edge * 0.45f : f == 0 ? 0.5f + edge * 0.5f : 0.35f + edge * 0.4f;
                    c.Set(x, y, v);
                }
                fr[f] = c.ToSprite("slash" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>砸地：贴着地面的扁圆冲击环，一圈圈扩大变稀，夹着飞起的碎块。</summary>
        public static Sprite[] Smash(int r) => Cached("sm_" + r, () =>
        {
            int w = r * 2 + 4, h = r + 4, mx = w / 2, my = h / 2; var fr = new Sprite[5];
            var rng = new System.Random(r * 3);
            for (int f = 0; f < 5; f++)
            {
                var c = new Canvas(w, h); float rr = r * (0.35f + f * 0.17f);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float dx = x - mx, dy = (y - my) * 2f, d = Mathf.Sqrt(dx * dx + dy * dy);   // 斜视角：竖向压一半
                    if (Mathf.Abs(d - rr) <= (f < 2 ? 1.6f : 1f) && (f < 3 || (x + y) % 2 == 0)) c.Set(x, y, f == 0 ? 1f : 0.85f - f * 0.12f);
                    else if (f == 0 && d < rr) c.Set(x, y, 0.6f);
                }
                for (int k = 0; k < 6 - f; k++)                                      // 碎块
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f; float rd = rr * (0.6f + (float)rng.NextDouble() * 0.5f);
                    c.Set(mx + Mathf.RoundToInt(Mathf.Cos(a) * rd), my + Mathf.RoundToInt(Mathf.Sin(a) * rd * 0.5f) + 1, 0.7f);
                }
                fr[f] = c.ToSprite("smash" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        // ---------------------------------------------------------------- 新加的特效
        /// <summary>烟团：导弹尾烟、砸地扬尘。实心圆 → 抖动网点 → 稀疏几点。</summary>
        public static Sprite[] Puff(int r) => Cached("pf_" + r, () =>
        {
            int s = r * 2 + 3, m = s / 2; var fr = new Sprite[4];
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(s, s); float rr = r * (0.6f + f * 0.15f);
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float d = new Vector2(x - m, y - m).magnitude;
                    if (d > rr) continue;
                    if (f == 1 && (x + y) % 3 == 0) continue;
                    if (f == 2 && (x + y) % 2 == 0) continue;
                    if (f == 3 && (x + 2 * y) % 4 != 0) continue;
                    c.Set(x, y, 0.6f - f * 0.1f);
                }
                fr[f] = c.ToSprite("puff" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>追踪弹拖出的光点：2×2 → 1 像素 → 暗点。</summary>
        public static Sprite[] Dot() => Cached("dot", () =>
        {
            var a = new Canvas(2, 2); a.Set(0, 0, 0.9f); a.Set(1, 0, 0.9f); a.Set(0, 1, 0.9f); a.Set(1, 1, 0.9f);
            var b = new Canvas(2, 2); b.Set(0, 0, 0.8f);
            var c = new Canvas(2, 2); c.Set(0, 0, 0.4f);
            return new[] { a.ToSprite("dot0", new Vector2(0.5f, 0.5f)), b.ToSprite("dot1", new Vector2(0.5f, 0.5f)), c.ToSprite("dot2", new Vector2(0.5f, 0.5f)) };
        });

        /// <summary>冲击环（左轮、轨道炮开火）：扁椭圆一圈圈扩大变稀。</summary>
        public static Sprite[] Ring(int r) => Cached("rg_" + r, () =>
        {
            int s = r * 2 + 3, m = s / 2; var fr = new Sprite[3];
            for (int f = 0; f < 3; f++)
            {
                var c = new Canvas(s, s); float rr = r * (0.45f + f * 0.27f);
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = (x - m) * 0.6f, dy = y - m, d = Mathf.Sqrt(dx * dx + dy * dy);   // 朝右开火：环沿竖向拉长
                    if (Mathf.Abs(d - rr * 0.8f) <= 0.8f && (f == 0 || (x + y) % 2 == 0)) c.Set(x, y, 0.9f - f * 0.2f);
                }
                fr[f] = c.ToSprite("ring" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>挂在着火敌人身上的小火苗，4 帧循环，支点在底边。</summary>
        public static Sprite[] BurnFlame() => Cached("burn", () =>
        {
            var fr = new Sprite[4];
            int[][] shapes =
            {
                new[] { 1, 2, 3, 3, 2, 2, 1, 1, 0, 0 },
                new[] { 1, 2, 3, 3, 3, 2, 1, 1, 1, 0 },
                new[] { 1, 2, 2, 3, 2, 2, 2, 1, 0, 0 },
                new[] { 1, 2, 3, 2, 2, 1, 1, 1, 1, 0 },
            };
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(9, 12); int sway = f == 1 ? 1 : f == 3 ? -1 : 0;
                for (int y = 0; y < 10; y++)
                {
                    int half = shapes[f][y]; int cx = 4 + (y > 5 ? sway : 0);
                    for (int x = -half; x <= half; x++) c.Set(cx + x, y, Mathf.Abs(x) <= half - 2 || y < 3 ? 1f : 0.65f);
                }
                c.Set(4 + sway, 11, 0.5f);                                             // 跳起来的一粒火星
                fr[f] = c.ToSprite("burn" + f, new Vector2(0.5f, 0f));
            }
            return fr;
        });

        /// <summary>单分子线：一道 1 像素的细线沿圆弧划过，线上几点闪光，再碎成虚线。</summary>
        public static Sprite[] WireSlash(int radius, float arc) => Cached("w_" + radius + "_" + arc, () =>
        {
            int s = radius * 2 + 4, m = s / 2; var fr = new Sprite[3];
            float half = arc * 0.5f;
            for (int f = 0; f < 3; f++)
            {
                var c = new Canvas(s, s);
                float a1 = f == 0 ? -half + arc * 0.6f : half;
                for (float a = -half; a <= a1; a += 0.5f)
                {
                    float rad = a * Mathf.Deg2Rad;
                    int x = m + Mathf.RoundToInt(Mathf.Cos(rad) * radius), y = m + Mathf.RoundToInt(Mathf.Sin(rad) * radius);
                    if (f == 2 && ((x + y) % 3 != 0)) continue;
                    c.Set(x, y, f == 1 ? 1f : 0.85f);
                    if (f == 1 && Mathf.Abs(Mathf.Repeat(a, 40f)) < 0.5f)                // 闪光点
                    {
                        c.Set(x + 1, y, 0.9f); c.Set(x - 1, y, 0.9f); c.Set(x, y + 1, 0.9f); c.Set(x, y - 1, 0.9f);
                    }
                }
                fr[f] = c.ToSprite("wire" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>
        /// 突刺：朝右刺出去的一道细长尖光。第 1 帧刺出一半，第 2 帧到头、枪尖最亮，第 3 帧变暗，第 4 帧碎成虚线。
        /// twin = 上下两道（双刺），thick = 粗一号（长枪）。
        /// </summary>
        public static Sprite[] Thrust(int len, bool twin, bool thick) => Cached("t_" + len + twin + thick, () =>
        {
            int h = twin ? 11 : thick ? 9 : 7, m = h / 2; var fr = new Sprite[4];
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(len + 4, h);
                int reach = f == 0 ? len / 2 : len;
                foreach (int cy in twin ? new[] { m - 3, m + 3 } : new[] { m })
                {
                    for (int x = 0; x < reach; x++)
                    {
                        if (f == 3 && x % 2 == 1) continue;
                        float u = x / (float)Mathf.Max(1, reach - 1);
                        float v = (f == 2 ? 0.55f : f == 3 ? 0.45f : 0.6f) + u * 0.4f;
                        c.Set(x, cy, v);
                        if (thick && x > reach / 4) { c.Set(x, cy - 1, v * 0.7f); c.Set(x, cy + 1, v * 0.7f); }
                    }
                    if (f < 3)                                                     // 枪尖：一个小菱形
                    {
                        int tx = reach;
                        c.Set(tx, cy, 1f); c.Set(tx + 1, cy, 0.9f); c.Set(tx - 1, cy - 1, 0.8f); c.Set(tx - 1, cy + 1, 0.8f);
                        if (thick) { c.Set(tx + 2, cy, 0.8f); c.Set(tx - 2, cy - 2, 0.6f); c.Set(tx - 2, cy + 2, 0.6f); }
                        if (f == 1) { c.Set(tx, cy - 2, 0.6f); c.Set(tx, cy + 2, 0.6f); }
                    }
                }
                fr[f] = c.ToSprite("thrust" + f, new Vector2(0f, 0.5f));
            }
            return fr;
        });

        /// <summary>拳套：往前推出去的扇形冲击波，三道弧一层层往外。</summary>
        public static Sprite[] Punch(int r) => Cached("pu_" + r, () =>
        {
            int s = r * 2 + 4, m = s / 2; var fr = new Sprite[4];
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(s, s);
                for (int k = 0; k < 3; k++)
                {
                    float rr = r * (0.25f + f * 0.22f + k * 0.15f);
                    if (rr > r) continue;
                    float spread = 28f + k * 8f;
                    for (float a = -spread; a <= spread; a += 1f)
                    {
                        float rad = a * Mathf.Deg2Rad;
                        int x = m + Mathf.RoundToInt(Mathf.Cos(rad) * rr), y = m + Mathf.RoundToInt(Mathf.Sin(rad) * rr);
                        if (f >= 2 && (x + y) % 2 == 0) continue;
                        c.Set(x, y, (k == 0 ? 1f : 0.7f) - f * 0.12f);
                        if (k == 0 && f < 2) c.Set(x - 1, y, 0.6f);
                    }
                }
                fr[f] = c.ToSprite("punch" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>X 形交叉命中（螳螂臂刃）。</summary>
        public static Sprite[] Cross(int r) => Cached("x_" + r, () =>
        {
            int s = r * 2 + 3, m = s / 2; var fr = new Sprite[3];
            for (int f = 0; f < 3; f++)
            {
                var c = new Canvas(s, s); int len = f == 0 ? r : f == 1 ? r : r / 2;
                for (int i = -len; i <= len; i++)
                {
                    if (f == 2 && i % 2 != 0) continue;
                    float v = 1f - Mathf.Abs(i) / (float)(len + 1) * 0.5f - f * 0.15f;
                    c.Set(m + i, m + i, v); c.Set(m + i, m - i, v);
                }
                fr[f] = c.ToSprite("cross" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>电爆：亮芯 + 往外乱窜的折线电丝，3 帧。</summary>
        public static Sprite[] Zap(int r) => Cached("z_" + r, () =>
        {
            int s = r * 2 + 3, m = s / 2; var fr = new Sprite[3];
            var rng = new System.Random(r * 7);
            for (int f = 0; f < 3; f++)
            {
                var c = new Canvas(s, s);
                if (f == 0) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) c.Set(m + x, m + y, 1f);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f + (float)rng.NextDouble(); float px = m, py = m;
                    int steps = Mathf.RoundToInt(r * (f == 0 ? 0.6f : 1f));
                    for (int st = 0; st < steps; st++)
                    {
                        a += (float)(rng.NextDouble() - 0.5) * 1.4f; px += Mathf.Cos(a); py += Mathf.Sin(a);
                        if (f == 2 && st % 2 == 0) continue;
                        c.Set(Mathf.RoundToInt(px), Mathf.RoundToInt(py), 0.95f - f * 0.2f);
                    }
                }
                fr[f] = c.ToSprite("zap" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>轨道炮的光束：从枪口一直到撞墙的地方，一条粗亮线迅速变细消失。</summary>
        public static void Beam(Vector2 a, Vector2 b, Color color, float widthPx, float life)
        {
            var go = new GameObject("Beam");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.sharedMaterial = Glow.MeshMat();
            lr.startColor = lr.endColor = color;
            lr.sortingOrder = 5002;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bf = go.AddComponent<BeamFade>();
            bf.width = widthPx / PPU; bf.life = life; bf.color = color;
        }

        /// <summary>弹壳：一颗黄铜色的小像素从枪身侧面弹出去，落地弹一下，躺一会儿消失。</summary>
        public static void Casing(Vector2 pos, Vector2 aim)
        {
            var go = new GameObject("Casing");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprites.Box(2, 1);
            sr.color = Sprites.Hex("d8a040");
            sr.sortingOrder = 4990;
            var c = go.AddComponent<CasingFly>();
            Vector2 side = Vector2.Perpendicular(aim) * (aim.x >= 0f ? 1f : -1f);           // 往枪身上方那一侧抛
            c.vel = (side * 2.2f - aim * 0.8f) + Random.insideUnitCircle * 0.6f;
            c.groundY = pos.y - 1.6f;
        }

        /// <summary>敌我光圈：贴地的扁椭圆细环，隔一个像素亮一点。</summary>
        public static Sprite GroundRing() => Cached("gring", () =>
        {
            int w = 40, h = 16; var c = new Canvas(w, h);
            float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float dx = (x - cx) / cx, dy = (y - cy) / cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1f || d < 0.78f) continue;
                c.Set(x, y, d > 0.9f ? ((x + y) % 2 == 0 ? 1f : 0.7f) : 0.35f);
            }
            return new[] { c.ToSprite("gring", new Vector2(0.5f, 0.5f)) };
        })[0];

        /// <summary>骇入的故障效果：一堆错位的横条和小方块，4 帧越来越稀。</summary>
        public static Sprite[] Glitch(int r) => Cached("gl_" + r, () =>
        {
            int s = r * 2 + 2; var fr = new Sprite[4];
            var rng = new System.Random(r * 17);
            for (int f = 0; f < 4; f++)
            {
                var c = new Canvas(s, s);
                int bars = 7 - f * 2;
                for (int k = 0; k < bars; k++)
                {
                    int y = rng.Next(s), x0 = rng.Next(s / 2), len = 3 + rng.Next(s / 2);
                    for (int x = x0; x < Mathf.Min(s, x0 + len); x++) if (f < 2 || x % 2 == 0) c.Set(x, y, 0.95f - f * 0.15f);
                }
                for (int k = 0; k < 4 - f; k++)
                {
                    int bx = rng.Next(s - 2), by = rng.Next(s - 2);
                    c.Set(bx, by, 0.8f); c.Set(bx + 1, by, 0.8f); c.Set(bx, by + 1, 0.8f); c.Set(bx + 1, by + 1, 0.8f);
                }
                fr[f] = c.ToSprite("glitch" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        /// <summary>水面上的小电弧：一道横着的细折线（6 种形状），第 2 帧碎成断续的点。</summary>
        public static Sprite[] PuddleArc(int v) => Cached("pa_" + v, () =>
        {
            int w = 12 + v * 2, h = 7; var fr = new Sprite[2];
            var rng = new System.Random(v * 13 + 5);
            var ys = new int[w]; int y = 3;
            for (int x = 0; x < w; x++) { if (x % 2 == 0) y = Mathf.Clamp(y + rng.Next(-2, 3), 0, h - 1); ys[x] = y; }
            int bx = 3 + rng.Next(w - 6);                                           // 一根分叉
            for (int f = 0; f < 2; f++)
            {
                var c = new Canvas(w, h);
                for (int x = 0; x < w; x++)
                {
                    if (f == 1 && x % 2 == 1) continue;
                    float v0 = f == 0 ? (x == 0 || x == w - 1 ? 0.6f : 1f) : 0.55f;
                    c.Set(x, ys[x], v0);
                    if (x > 0 && Mathf.Abs(ys[x] - ys[x - 1]) > 1) c.Set(x, (ys[x] + ys[x - 1]) / 2, v0 * 0.9f);
                }
                if (f == 0) { c.Set(bx, Mathf.Clamp(ys[bx] + 1, 0, h - 1), 0.7f); c.Set(bx + 1, Mathf.Clamp(ys[bx] + 2, 0, h - 1), 0.5f); }
                fr[f] = c.ToSprite("parc" + f, new Vector2(0.5f, 0.5f));
            }
            return fr;
        });

        // ---------------------------------------------------------------- 播放
        /// <summary>在 pos 播一段帧动画：angle 是朝向（度），flipY 用来让来回两刀方向相反。播完自己销毁。</summary>
        public static SpriteRenderer Play(Sprite[] frames, Vector2 pos, float angle, Color color, float fps, int order = 6000, bool flipY = false, Transform follow = null, bool additive = true)
        {
            if (frames == null || frames.Length == 0) return null;
            var go = new GameObject("Fx");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.sharedMaterial = additive ? Glow.Mat() : Sprites.Unlit;          // 不发光的特效（无铭的黑色刀刃）用普通材质
            sr.color = color;
            sr.sortingOrder = order;
            sr.flipY = flipY;
            var a = go.AddComponent<FxAnim>();
            a.frames = frames; a.fps = fps; a.follow = follow; a.offset = follow != null ? pos - (Vector2)follow.position : Vector2.zero;
            return sr;
        }
    }

    /// <summary>按帧播放、播完销毁；follow 不为空时跟着目标走（刀光跟着人）。</summary>
    public class FxAnim : MonoBehaviour
    {
        public Sprite[] frames; public float fps = 20f; public Transform follow; public Vector2 offset;
        public bool loop; public float life = -1f;
        SpriteRenderer sr; float t;
        void Awake() { sr = GetComponent<SpriteRenderer>(); }
        void LateUpdate()
        {
            t += Time.deltaTime;
            if (follow != null) transform.position = (Vector2)follow.position + offset;
            int i = Mathf.FloorToInt(t * fps);
            if (loop) { sr.sprite = frames[i % frames.Length]; if (life > 0f && t >= life) Destroy(gameObject); return; }
            if (i >= frames.Length) { Destroy(gameObject); return; }
            sr.sprite = frames[i];
        }
    }

    /// <summary>光束：宽度和亮度一起掉下去。</summary>
    public class BeamFade : MonoBehaviour
    {
        public float width, life; public Color color;
        LineRenderer lr; float t;
        void Awake() { lr = GetComponent<LineRenderer>(); }
        void Update()
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / life);
            lr.startWidth = lr.endWidth = Mathf.Max(1f / Sprites.PPU, width * (1f - u));
            var c = color; c.a = 1f - u * u;
            lr.startColor = lr.endColor = c;
            if (t >= life) Destroy(gameObject);
        }
    }

    /// <summary>弹壳：抛物线落到脚边，弹一下，躺 0.8 秒后消失。</summary>
    public class CasingFly : MonoBehaviour
    {
        public Vector2 vel; public float groundY;
        float t; int bounces;
        void Update()
        {
            t += Time.deltaTime;
            vel.y -= 14f * Time.deltaTime;
            var p = (Vector2)transform.position + vel * Time.deltaTime;
            if (p.y < groundY && vel.y < 0f)
            {
                p.y = groundY;
                if (bounces++ < 1) { vel = new Vector2(vel.x * 0.4f, -vel.y * 0.35f); }
                else vel = Vector2.zero;
            }
            transform.position = p;
            transform.rotation = Quaternion.Euler(0, 0, vel.sqrMagnitude > 0.01f ? t * 900f : 0f);
            if (t > 1.4f) Destroy(gameObject);
        }
    }
}
