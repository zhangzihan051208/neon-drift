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

        // ---------------------------------------------------------------- 播放
        /// <summary>在 pos 播一段帧动画：angle 是朝向（度），flipY 用来让来回两刀方向相反。播完自己销毁。</summary>
        public static SpriteRenderer Play(Sprite[] frames, Vector2 pos, float angle, Color color, float fps, int order = 6000, bool flipY = false, Transform follow = null)
        {
            if (frames == null || frames.Length == 0) return null;
            var go = new GameObject("Fx");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.sharedMaterial = Glow.Mat();
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
}
