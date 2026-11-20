using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 梯子和它背后那面墙的像素画（代码逐像素生成）。墙嵌在后墙那排房子留出的缺口里（沿 x 方向，3.4 格宽），
    /// 高度按左右两栋邻居定；左端露出一点墙厚，顶上有一条压顶。不同去处的梯子样式不同：
    ///   小怪房 / 下一间：带污渍和裂缝的水泥墙 + 生锈铁梯，缺了几根横档
    ///   黑市：补丁摞补丁的旧砖墙 + 绳子绑的木梯，上面挂着老鬼的洋红霓虹招牌
    ///   随机事件：塌了一个大洞的破砖墙 + 挂下来的绳梯，墙上喷着一个滴漆的问号
    ///   幽灵频道：水泥墙上码满报废的显像管屏幕（几块在闪雪花）+ 缠满电缆的钢梯
    ///   Boss：铆钉钢板隔墙 + 底部黄黑警示条 + 带护笼的工业爬梯，顶上一盏红灯
    /// 发光的部分（霓虹、屏幕、灯）画在另一张图上，用发光材质叠上去。
    /// </summary>
    public static class LadderArt
    {
        public const float WallTiles = 3.4f, LadderOffset = 1.1f, LadderTiles = 1.2f, HeightUnits = 10f;
        /// <summary>墙有多厚（格）：像一栋矮楼一样有进深，顶上是平屋顶，右端露出侧面，把后面的东西全挡住。</summary>
        public const float WallDepth = 2.5f;
        const int PPU = Sprites.PPU;

        class Canvas
        {
            public int w, h; public Color32[] px, glow;
            public Canvas(int w, int h) { this.w = w; this.h = h; px = new Color32[w * h]; glow = new Color32[w * h]; }
            public void Set(int x, int y, Color c) { if (x < 0 || y < 0 || x >= w || y >= h) return; px[y * w + x] = c; }
            public void Glow(int x, int y, Color c) { if (x < 0 || y < 0 || x >= w || y >= h) return; glow[y * w + x] = c; }
            public Color Get(int x, int y) => (x < 0 || y < 0 || x >= w || y >= h) ? Color.clear : (Color)px[y * w + x];
        }

        // 墙面坐标（u 沿墙往右，v 往上，单位像素）和图上像素的换算
        static int L = 10, Wpx, Hpx, Skew, Top = 48;
        static float BaseY(float u) => Skew - u * 0.5f;

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }
        static Color C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static Color Mul(Color c, float k) { c.r *= k; c.g *= k; c.b *= k; c.a = 1f; return c; }

        /// <summary>在墙面坐标 (u, v) 画一点。</summary>
        static void F(Canvas c, float u, float v, Color col) => c.Set(L + Mathf.RoundToInt(u), Mathf.RoundToInt(BaseY(u) + v), col);
        static void FG(Canvas c, float u, float v, Color col) => c.Glow(L + Mathf.RoundToInt(u), Mathf.RoundToInt(BaseY(u) + v), col);

        public class Result { public Sprite main, glow; public Vector2 lamp; public Color lampCol; public bool flicker; }

        public static Result Make(RoomKind kind, bool combatFork, int seed, float heightUnits = HeightUnits)
        {
            Wpx = Mathf.RoundToInt(WallTiles * PPU); Hpx = Mathf.RoundToInt(heightUnits * PPU); Skew = Wpx / 2;
            int Dx = Mathf.RoundToInt(WallDepth * PPU), Dy = Mathf.RoundToInt(WallDepth * PPU * 0.5f);   // 进深在图上往右上方伸出去多少像素
            var c = new Canvas(L + Wpx + Dx + 4, Skew + Hpx + Dy + Top);
            var rng = new System.Random(seed);
            int u0 = Mathf.RoundToInt(LadderOffset * PPU), lw = Mathf.RoundToInt(LadderTiles * PPU);

            // 墙顶高度（砖墙顶上是碎的，参差不齐）
            var top = new int[Wpx];
            bool brick = kind == RoomKind.Shop || kind == RoomKind.Event;
            for (int u = 0; u < Wpx; u++)
                top[u] = Hpx - (brick ? (int)(Mathf.PerlinNoise(u * 0.09f, seed * 0.13f) * 22f) + (u % 6 < 3 && Hash(u / 6, 0, seed) < 0.3f ? 6 : 0) : 0);

            // ---------------- 墙面
            for (int x = L; x < L + Wpx; x++)
            {
                int u = x - L; float b = BaseY(u);
                for (int y = Mathf.CeilToInt(b); y < b + top[u]; y++)
                {
                    int v = y - Mathf.CeilToInt(b);
                    Color col = kind switch
                    {
                        RoomKind.Shop => Brick(u, v, seed, 0.35f),
                        RoomKind.Event => Brick(u, v, seed, 0.08f),
                        RoomKind.Boss => Steel(u, v, seed),
                        _ => Concrete(u, v, seed),
                    };
                    float grime = Mathf.Clamp01(1f - v / 60f);                       // 墙根发黑
                    col = Mul(col, 1f - grime * 0.45f);
                    float soot = Mathf.Clamp01((v - (top[u] - 40)) / 40f);           // 墙顶被雨水和烟熏黑
                    col = Mul(col, 1f - soot * 0.25f);
                    if (Mathf.PerlinNoise(u * 0.11f + seed * 0.3f, v * 0.03f) > 0.66f) col = Mul(col, 0.78f);   // 竖着往下的脏印子
                    c.Set(x, y, col);
                }
            }
            // 水渍和锈迹：从墙顶或支架往下流的几道竖条
            int streaks = 4 + rng.Next(4);
            for (int i = 0; i < streaks; i++)
            {
                int su = rng.Next(Wpx); if (su >= u0 - 2 && su <= u0 + lw + 2) su = (su + lw + 8) % Wpx;
                int len = 40 + rng.Next(170), v1 = top[su] - rng.Next(10);
                bool rust = rng.NextDouble() < 0.4;
                for (int v = v1; v > v1 - len && v > 0; v--)
                {
                    float k = 1f - (v1 - v) / (float)len;
                    for (int du = 0; du < (rng.NextDouble() < 0.3 ? 2 : 1); du++)
                    {
                        Color o = c.Get(L + su + du, Mathf.RoundToInt(BaseY(su + du) + v));
                        if (o.a < 0.5f) continue;
                        Color t = rust ? Color.Lerp(o, C(120, 64, 34), 0.45f * k) : Mul(o, 1f - 0.28f * k);
                        F(c, su + du, v, t);
                    }
                }
            }
            // 裂缝
            if (kind != RoomKind.Boss)
                for (int i = 0; i < 3; i++)
                {
                    float u = rng.Next(Wpx), v = rng.NextDouble() < 0.5 ? top[(int)u] - 2 : 6 + rng.Next(40);
                    float dir = v > Hpx / 2 ? -1f : 1f;
                    for (int s = 0; s < 30 + rng.Next(40); s++)
                    {
                        u += (float)(rng.NextDouble() - 0.5) * 2.2f; v += dir;
                        if (u < 0 || u >= Wpx || v < 1 || v >= top[Mathf.Clamp((int)u, 0, Wpx - 1)]) break;
                        F(c, u, v, C(34, 31, 29));
                    }
                }

            // ---------------- 各自的布景
            switch (kind)
            {
                case RoomKind.Shop: ShopDeco(c, rng, u0, lw, top); break;
                case RoomKind.Event: EventDeco(c, rng, u0, lw, top, seed); break;
                case RoomKind.Signal: SignalDeco(c, rng, u0, lw, top); break;
                case RoomKind.Boss: BossDeco(c, rng, top); break;
            }

            // ---------------- 涂鸦：住在积水区的人写的抱怨
            Graffiti(c, rng, kind, u0, lw, top);

            // ---------------- 右端的侧面（镜头看得到的那一端，往后延伸 WallDepth 格）
            for (int x = L + Wpx; x < L + Wpx + Dx; x++)
            {
                float d = (x - L - Wpx) / (float)PPU;                                 // 0 → WallDepth
                float b = BaseY(Wpx) + 16f * d;
                int h = top[Wpx - 1];
                for (int y = Mathf.CeilToInt(b); y < b + h; y++)
                {
                    int v = y - Mathf.CeilToInt(b);
                    Color col = kind switch
                    {
                        RoomKind.Shop => Brick(x + 400, v, seed, 0.35f),
                        RoomKind.Event => Brick(x + 400, v, seed, 0.08f),
                        RoomKind.Boss => Steel(x + 400, v, seed),
                        _ => Concrete(x + 400, v, seed),
                    };
                    col = Mul(col, 0.58f * (1f - Mathf.Clamp01(1f - v / 60f) * 0.4f));     // 侧面背光，更暗；墙根更黑
                    c.Set(x, y, col);
                }
            }
            // ---------------- 平屋顶：沥青油毡，前沿一道压顶，有积水和几样杂物
            Color roof = kind == RoomKind.Boss ? C(40, 41, 44) : C(34, 32, 31), lip = kind == RoomKind.Boss ? C(58, 60, 64) : brick ? C(62, 46, 38) : C(66, 64, 60);
            for (float u = 0; u < Wpx; u += 0.5f)
                for (float d = 0; d <= WallDepth; d += 0.5f / PPU)
                {
                    int tu = Mathf.Clamp((int)u, 0, Wpx - 1);
                    int px = L + Mathf.RoundToInt(u + PPU * d), py = Mathf.RoundToInt(BaseY(u) + top[tu] + 16f * d);
                    Color col;
                    if (d < 0.12f || d > WallDepth - 0.08f) col = lip;                               // 前后两道压顶
                    else
                    {
                        float n = Hash(px, py, seed + 21);
                        col = Mul(roof, 0.9f + n * 0.15f);
                        if (Mathf.PerlinNoise(px * 0.08f + seed, py * 0.12f) > 0.68f) col = C(22, 22, 24);      // 屋顶积水
                    }
                    c.Set(px, py, col);
                }
            // 屋顶上的杂物：一台锈掉的空调外机和一截管子（只在平顶的墙上）
            if (!brick)
            {
                float au = Wpx * 0.72f, ad = WallDepth * 0.5f;
                for (int k = 0; k < 14; k++)
                    for (int hgt = 0; hgt < 10; hgt++)
                        F(c, au + k + PPU * ad, top[Mathf.Clamp((int)au, 0, Wpx - 1)] + 16f * ad + hgt + k * 0f, hgt == 9 ? C(70, 68, 64) : Mul(C(60, 54, 48), 0.8f + (k % 3) * 0.08f));
                for (int k = 0; k < 40; k++)
                    F(c, 6 + k * 0.5f + PPU * (WallDepth * 0.3f + k * 0.03f), top[6] + 16f * (WallDepth * 0.3f + k * 0.03f) + 2, C(48, 40, 34));
            }

            // ---------------- 梯子
            var res = new Result();
            switch (kind)
            {
                case RoomKind.Shop: WoodLadder(c, rng, u0, lw); res.lampCol = Sprites.Hex("e0409a"); res.flicker = true; break;
                case RoomKind.Event: RopeLadder(c, rng, u0, lw); res.lampCol = Sprites.Hex("d8e060"); break;
                case RoomKind.Signal: CableLadder(c, rng, u0, lw); res.lampCol = Sprites.Hex("ff4fd8"); res.flicker = true; break;
                case RoomKind.Boss: CageLadder(c, rng, u0, lw); res.lampCol = Sprites.Hex("ff3a3a"); res.flicker = true; break;
                default: IronLadder(c, rng, u0, lw, combatFork); res.lampCol = Sprites.Hex("ffb070"); break;
            }

            var pivot = new Vector2(L / (float)c.w, Skew / (float)c.h);
            res.main = ToSprite(c.px, c.w, c.h, pivot, "LadderWall");
            res.glow = ToSprite(c.glow, c.w, c.h, pivot, "LadderGlow");
            res.lamp = new Vector2((u0 + lw / 2f) / PPU, (-(u0 + lw / 2f) * 0.5f + Hpx - 8) / PPU);       // 相对左下角（墙的起点）
            return res;
        }

        static Sprite ToSprite(Color32[] px, int w, int h, Vector2 pivot, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PPU, 0, SpriteMeshType.FullRect);
        }

        // ================================================================ 墙面材质
        static Color Concrete(int u, int v, int s)
        {
            float n = Hash(u, v, s);
            Color col = C(56, 54, 52);
            col = Mul(col, 0.9f + n * 0.14f);
            if (Mathf.PerlinNoise(u * 0.05f + s, v * 0.05f) > 0.6f) col = Mul(col, 0.82f);      // 大块的潮斑
            if (v % 64 == 0) col = Mul(col, 0.7f);                                                  // 浇筑的接缝
            if (u % 36 == 8 && v % 32 == 14) col = C(24, 23, 22);                                   // 模板拉杆洞
            if (n > 0.985f) col = C(78, 76, 72);
            return col;
        }

        static Color Brick(int u, int v, int s, float plaster)
        {
            int row = v / 7, off = row % 2 == 0 ? 0 : 6;
            int bx = (u + off) / 12;
            bool mortar = v % 7 == 0 || (u + off) % 12 == 0;
            float pn = Mathf.PerlinNoise(u * 0.04f + s * 0.7f, v * 0.04f);
            if (pn < plaster)                                                                      // 抹过水泥的补丁
                return Mul(C(66, 63, 60), 0.9f + Hash(u, v, s) * 0.12f);
            if (mortar) return C(40, 37, 35);
            float h = Hash(bx, row, s);
            if (h < 0.05f) return C(18, 15, 14);                                                   // 掉了的砖
            Color b = h < 0.35f ? C(80, 42, 34) : h < 0.7f ? C(68, 36, 30) : C(90, 50, 40);
            return Mul(b, 0.9f + Hash(u, v, s + 1) * 0.15f);
        }

        static Color Steel(int u, int v, int s)
        {
            Color col = Mul(C(44, 45, 48), 0.92f + Hash(u, v, s) * 0.1f);
            int pu = u % 34, pv = v % 44;
            if (pu == 0 || pv == 0) return C(24, 24, 26);                                        // 钢板缝
            if ((pu == 4 || pu == 30) && pv % 8 == 4) return C(82, 82, 84);                      // 铆钉
            if ((pu == 4 || pu == 30) && pv % 8 == 3) return C(18, 18, 20);
            if (v < 26 && (u + v) / 7 % 2 == 0) return Mul(C(140, 110, 34), 0.75f + Hash(u, v, s) * 0.25f);  // 黄黑警示条（掉漆）
            if (v < 26) return C(20, 20, 22);
            if (Mathf.PerlinNoise(u * 0.07f, v * 0.07f + s) > 0.7f) col = Color.Lerp(col, C(112, 60, 34), 0.4f);
            return col;
        }

        // ================================================================ 布景
        static void ShopDeco(Canvas c, System.Random rng, int u0, int lw, int[] top)
        {
            // 霓虹招牌：梯子顶上一块黑底牌子，洋红色的灯管写着一个信用点符号和往上的箭头
            int su = u0 - 6, sv = Hpx - 64, sw = lw + 12, sh = 30;
            for (int u = su; u < su + sw; u++) for (int v = sv; v < sv + sh; v++) F(c, u, v, C(24, 20, 26));
            Color neon = Sprites.Hex("ff4fd8"), tube = C(90, 30, 70);
            void N(float u, float v) { F(c, u, v, tube); FG(c, u, v, neon); }
            for (int i = 0; i < 360; i += 6)                                                   // ₡：一个缺口的圆 + 两道竖线
            {
                if (i > 320 || i < 40) continue;
                float a = i * Mathf.Deg2Rad;
                N(su + 12 + Mathf.Cos(a) * 7f, sv + 15 + Mathf.Sin(a) * 9f);
            }
            for (int v = sv + 5; v < sv + 26; v++) { N(su + 11, v); N(su + 14, v); }
            for (int v = sv + 5; v < sv + 26; v++) N(su + sw - 12, v);                       // ↑
            for (int k = 0; k < 6; k++) { N(su + sw - 12 - k, sv + 25 - k); N(su + sw - 12 + k, sv + 25 - k); }
            // 晾着的一块破布
            for (int u = u0 + lw + 6; u < u0 + lw + 22; u++) for (int v = 120; v > 100 - (u % 5); v--) F(c, u, v, (u + v) % 4 == 0 ? C(70, 40, 60) : C(96, 54, 80));
        }

        static void EventDeco(Canvas c, System.Random rng, int u0, int lw, int[] top, int seed)
        {
            // 墙上塌的大洞：深色的里子，边缘是碎砖
            float hu = u0 + lw + 14, hv = 150, rx = 13, ry = 22;
            for (int u = (int)(hu - rx - 3); u < hu + rx + 3; u++)
                for (int v = (int)(hv - ry - 3); v < hv + ry + 3; v++)
                {
                    float d = Mathf.Sqrt((u - hu) * (u - hu) / (rx * rx) + (v - hv) * (v - hv) / (ry * ry)) + (Hash(u, v, seed) - 0.5f) * 0.25f;
                    if (d < 1f) F(c, u, v, d < 0.8f ? C(18, 14, 14) : C(52, 30, 24));
                }
            // 滴着漆的问号（黄绿色），喷在梯子左边
            Color paint = C(176, 200, 64);
            float qu = 8, qv = 190;
            for (int i = -40; i <= 220; i += 4)
            {
                float a = i * Mathf.Deg2Rad;
                for (int t = 0; t < 2; t++) F(c, qu + 10 + Mathf.Cos(a) * (8 + t), qv + 14 + Mathf.Sin(a) * (10 + t), paint);
            }
            for (int v = (int)qv - 8; v < qv + 4; v++) { F(c, qu + 10, v, paint); F(c, qu + 11, v, paint); }
            for (int du = 0; du < 3; du++) for (int dv = 0; dv < 3; dv++) F(c, qu + 9 + du, qv - 16 + dv, paint);
            for (int k = 0; k < 4; k++)                                                         // 漆往下流
            {
                float du = qu + 4 + rng.Next(14); int len = 6 + rng.Next(16);
                for (int v = 0; v < len; v++) F(c, du, qv + 4 - v - (k == 0 ? 20 : 0), Mul(paint, 0.85f));
            }
        }

        static void SignalDeco(Canvas c, System.Random rng, int u0, int lw, int[] top)
        {
            // 报废的显像管屏幕码成一面墙，几块还亮着雪花
            Color glow = Sprites.Hex("ff4fd8");
            for (int v = 30; v < Hpx - 30; v += 22)
                for (int u = 2; u < Wpx - 22; u += 24)
                {
                    if (u > u0 - 22 && u < u0 + lw + 2) continue;                              // 梯子那一溜不放
                    if (rng.NextDouble() < 0.25) continue;
                    int w = 20, h = 18;
                    for (int du = 0; du < w; du++) for (int dv = 0; dv < h; dv++)
                    {
                        bool edge = du < 2 || du >= w - 2 || dv < 2 || dv >= h - 3;
                        F(c, u + du, v + dv, edge ? C(44, 42, 48) : C(16, 18, 20));
                    }
                    bool on = rng.NextDouble() < 0.3;
                    if (on)
                        for (int du = 3; du < w - 3; du++) for (int dv = 4; dv < h - 3; dv++)
                            if (rng.NextDouble() < 0.45) FG(c, u + du, v + dv, Color.Lerp(glow, Color.white, (float)rng.NextDouble() * 0.6f));
                }
            // 从墙顶垂下来的电缆
            for (int k = 0; k < 5; k++)
            {
                float u = rng.Next(Wpx), sag = 20 + rng.Next(60);
                for (int t = 0; t < 40; t++) F(c, u + t * 0.6f, top[Mathf.Clamp((int)u, 0, Wpx - 1)] - 2 - Mathf.Sin(t / 40f * Mathf.PI) * sag, C(20, 20, 24));
            }
        }

        static void BossDeco(Canvas c, System.Random rng, int[] top)
        {
            // 顶上一道刷着骷髅色块的警示带
            for (int u = 0; u < Wpx; u++) for (int v = Hpx - 20; v < Hpx - 12; v++) F(c, u, v, (u / 8) % 2 == 0 ? C(150, 40, 36) : C(40, 18, 18));
        }

        // ================================================================ 涂鸦（英文，自带 5×7 像素字体，硬边）
        static readonly string[] Complaints =
        {
            "THE DOME EATS US", "GIVE US POWER", "UPLOAD = DEATH", "RAIN NEVER STOPS", "THE ARK DOESNT WANT US",
            "WE ARE NOT DATA", "IRONMANE GO HOME", "WHO STOLE THE PUMP", "DONT TRUST CLINICS", "DRAIN KING LIES",
            "WET FEET 3 MONTHS", "FIXED NEXT WEEK?", "BORN IN DEBT", "THEY ARE LISTENING", "NO WATER BILL NO LIFE",
        };
        static readonly string[] ShopLines = { "GHOST SELLS FAKES", "CASH ONLY", "NO CREDIT OR ELSE" };
        static readonly string[] SignalLines = { "DONT ANSWER IT", "THE SCREENS WATCH", "VOICES ON CH 0" };
        static readonly string[] EventLines = { "DONT LOOK BACK", "SOMETHING IN THE HOLE" };
        static readonly string[] BossLines = { "NO ENTRY", "PUMP STATION 4" };

        // 喷漆颜色：都是褪色、发脏的颜色，不用蓝青色
        static readonly Color[] Paints = { C(130, 40, 34), C(150, 126, 52), C(140, 134, 124), C(116, 50, 100), C(104, 112, 58), C(150, 74, 40) };

        // 5×7 像素字体：每个字 7 行，每行 5 位，'#' 是笔画
        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".####", "#....", "#....", "#.###", "#...#", "#...#", ".###." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
            ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
            ['!'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." },
            ['='] = new[] { ".....", ".....", "#####", ".....", "#####", ".....", "....." },
            ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." },
        };

        /// <summary>
        /// 横着喷一行字（沿着墙走），scale 是放大倍数（2 = 每个字 10×14 像素）。
        /// 喷漆效果：笔画外有零星飞溅，有几道往下流的漆，部分被雨冲掉。
        /// </summary>
        static void Spray(Canvas c, System.Random rng, string text, float u, float v, int scale, Color paint)
        {
            float x = u;
            foreach (char ch in text)
            {
                if (ch == ' ' || !Glyphs.TryGetValue(ch, out var g)) { x += 4 * scale; continue; }
                float wob = (float)(rng.NextDouble() - 0.5) * scale;                          // 手喷的，上下有点歪
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (g[row][col] != '#') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                if (rng.NextDouble() < 0.08) continue;                            // 被雨冲掉
                                float pu = x + col * scale + sx, pv = v + wob + (6 - row) * scale + sy;
                                F(c, pu, pv, Mul(paint, 0.85f + (float)rng.NextDouble() * 0.25f));
                                if (rng.NextDouble() < 0.025) F(c, pu + rng.Next(-2, 3), pv + rng.Next(-2, 3), Mul(paint, 0.7f));
                                if (row == 6 && sy == 0 && rng.NextDouble() < 0.12)               // 往下流的漆
                                {
                                    int len = 3 + rng.Next(8 * scale);
                                    for (int d = 1; d < len; d++) F(c, pu, pv - d, Mul(paint, 0.85f - d * 0.02f));
                                }
                            }
                    }
                x += 6 * scale;
            }
        }

        /// <summary>按单词换行，每行最多 maxChars 个字。</summary>
        static List<string> Wrap(string text, int maxChars)
        {
            var lines = new List<string>(); string cur = "";
            foreach (var w in text.Split(' '))
            {
                if (cur.Length == 0) cur = w;
                else if (cur.Length + 1 + w.Length <= maxChars) cur += " " + w;
                else { lines.Add(cur); cur = w; }
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        /// <summary>
        /// 墙上的涂鸦：一句大字的抱怨（喷在整面墙上，梯子挡在它前面），再加一行小字记着积水的天数、
        /// 一组“正”字一样的划痕和一道乱画的线。黑市、幽灵频道、随机事件、Boss 的墙写的内容不一样。
        /// </summary>
        static void Graffiti(Canvas c, System.Random rng, RoomKind kind, int u0, int lw, int[] top)
        {
            string Pick(string[] a) => a[rng.Next(a.Length)];
            string big = kind switch
            {
                RoomKind.Shop => Pick(ShopLines),
                RoomKind.Signal => Pick(SignalLines),
                RoomKind.Event => Pick(EventLines),
                RoomKind.Boss => BossLines[0],
                _ => Pick(Complaints),
            };
            Color paint = kind == RoomKind.Boss ? C(150, 126, 52) : Paints[rng.Next(Paints.Length)];
            int scale = 2, maxChars = (Wpx - 6) / (6 * scale);                                // 大字一行最多 9 个
            var lines = Wrap(big, maxChars);
            if (lines.Count > 3) { scale = 1; maxChars = (Wpx - 6) / 6; lines = Wrap(big, maxChars); }
            float v = Hpx * (0.5f + (float)rng.NextDouble() * 0.15f);
            if (kind == RoomKind.Event) v = 110 + lines.Count * 16;                          // 避开墙上的洞
            foreach (var line in lines)
            {
                float width = line.Length * 6 * scale;
                Spray(c, rng, line, Mathf.Max(3f, (Wpx - width) * 0.5f + rng.Next(-4, 5)), v, scale, paint);
                v -= 8 * scale + 2;
            }
            // 小字：积水第几天 / Boss 墙上的第二行
            string small = kind == RoomKind.Boss ? BossLines[1] : "FLOOD DAY " + (200 + rng.Next(700));
            if (kind != RoomKind.Signal) Spray(c, rng, small, 4 + rng.Next(6), 46 + rng.Next(20), 1, Mul(C(140, 134, 124), 0.75f));
            // 记天数的划痕：四竖一横一组
            int tu = Wpx - 34 + rng.Next(6), tv = 70 + rng.Next(40);
            Color mark = C(120, 116, 108);
            for (int g = 0; g < 2 + rng.Next(2); g++)
            {
                int bu = tu + g * 10;
                for (int k = 0; k < 4; k++) for (int h = 0; h < 7; h++) F(c, bu + k * 2, tv + h, mark);
                for (int k = 0; k < 9; k++) F(c, bu - 1 + k, tv + 1 + k * 0.6f, mark);
            }
            Color sc = Paints[rng.Next(Paints.Length)];
            float su = 6 + rng.Next(20), sv = 24 + rng.Next(16);                             // 一道乱画的弧线
            for (int k = 0; k < 40; k++) F(c, su + k * 0.6f, sv + Mathf.Sin(k * 0.35f) * 4f, Mul(sc, 0.8f));
        }

        // ================================================================ 梯子
        static void Rails(Canvas c, int u0, int lw, int thick, int vTop, System.Func<int, int, Color> col, System.Func<int, float> wobble = null)
        {
            for (int side = 0; side < 2; side++)
            {
                int ru = side == 0 ? u0 + 2 : u0 + lw - 2 - thick;
                for (int v = 2; v < vTop; v++)
                {
                    float wob = wobble != null ? wobble(v) : 0f;
                    for (int t = 0; t < thick; t++) F(c, ru + t + wob, v, col(side * 100 + t, v));
                    F(c, ru + thick + wob, v - 1, C(22, 20, 20));                                // 投在墙上的影子
                }
            }
        }

        static void IronLadder(Canvas c, System.Random rng, int u0, int lw, bool fork)
        {
            Color rust = C(150, 86, 46), iron = C(84, 56, 40), dark = C(54, 36, 28);
            int vTop = Hpx + 26;
            Rails(c, u0, lw, 3, vTop, (k, v) =>
            {
                float n = Mathf.PerlinNoise(k * 0.3f, v * 0.06f);
                return n > 0.62f ? rust : n < 0.3f ? dark : iron;
            });
            for (int v = 14; v < vTop - 6; v += 21)
            {
                if (rng.NextDouble() < 0.15) { F(c, u0 + 6, v, rust); F(c, u0 + 7, v - 1, rust); continue; }   // 断掉的横档，只剩一截
                int bend = rng.NextDouble() < 0.15 ? 2 : 0;
                for (int u = u0 + 5; u < u0 + lw - 5; u++)
                {
                    float t = (u - u0) / (float)lw;
                    int dv = Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * -bend);
                    Color col = Hash(u, v, 3) < 0.3f ? rust : iron;
                    F(c, u, v + dv, col); F(c, u, v + dv + 1, Mul(col, 0.8f)); F(c, u, v + dv - 1, C(26, 24, 22));
                }
            }
            for (int v = 50; v < Hpx; v += 70)                                                 // 固定支架 + 往下流的锈
                for (int side = 0; side < 2; side++)
                {
                    int bu = side == 0 ? u0 - 2 : u0 + lw + 1;
                    for (int du = 0; du < 4; du++) for (int dv = 0; dv < 3; dv++) F(c, bu + du, v + dv, dark);
                    for (int dv = 0; dv < 14 + (v % 9); dv++) F(c, bu + 1, v - dv, Color.Lerp(c.Get(L + bu + 1, Mathf.RoundToInt(BaseY(bu + 1) + v - dv)), rust, 0.5f * (1f - dv / 24f)));
                }
            // 顶上两只扶手往后弯
            for (int side = 0; side < 2; side++)
            {
                int ru = side == 0 ? u0 + 2 : u0 + lw - 5;
                for (int i = 0; i < 12; i++) F(c, ru + 1 + i * 0.6f, vTop + Mathf.Sin(i / 12f * Mathf.PI) * 6f, iron);
            }
            // 岔路的“小怪房”梯子旁边用红漆刷一个叉
            if (fork)
                for (int k = -6; k <= 6; k++) { F(c, u0 + lw + 14 + k, 160 + k, C(150, 34, 30)); F(c, u0 + lw + 14 + k, 160 - k, C(150, 34, 30)); }
        }

        static void WoodLadder(Canvas c, System.Random rng, int u0, int lw)
        {
            Color wood = C(110, 78, 48), wood2 = C(88, 62, 38), rope = C(180, 160, 120);
            int vTop = Hpx - 70;                                                                 // 搭到招牌下面
            Rails(c, u0, lw, 4, vTop, (k, v) => (v / 9 + k) % 5 == 0 ? wood2 : Mul(wood, 0.9f + Hash(k, v, 5) * 0.15f), v => (v > 120 ? 1 : 0));
            for (int v = 16; v < vTop - 4; v += 18 + rng.Next(6))
            {
                for (int u = u0 + 5; u < u0 + lw - 5; u++) { F(c, u, v, Hash(u, v, 7) < 0.2f ? wood2 : C(128, 92, 56)); F(c, u, v + 1, wood2); F(c, u, v - 1, C(26, 22, 20)); }
                for (int side = 0; side < 2; side++)                                              // 绳子绑的地方
                {
                    int ru = side == 0 ? u0 + 3 : u0 + lw - 6;
                    F(c, ru, v + 2, rope); F(c, ru + 2, v + 1, rope); F(c, ru + 1, v - 1, rope); F(c, ru + 3, v, rope);
                }
            }
        }

        static void RopeLadder(Canvas c, System.Random rng, int u0, int lw)
        {
            Color rope = C(150, 128, 92), rung = C(108, 78, 50);
            int vTop = Hpx + 4;
            float Wob(int v) => Mathf.Sin(v / 22f) * 2.2f * (1f - v / (float)Hpx);
            Rails(c, u0, lw, 2, vTop, (k, v) => (v % 4 == 0) ? Mul(rope, 0.8f) : rope, Wob);
            for (int v = 18; v < vTop - 6; v += 26)
                for (int u = u0 + 4; u < u0 + lw - 4; u++) { F(c, u + Wob(v), v, rung); F(c, u + Wob(v), v - 1, C(26, 22, 20)); }
            for (int du = -3; du <= lw + 3; du++) for (int dv = 0; dv < 3; dv++) F(c, u0 + du, Hpx + 2 + dv, C(60, 58, 56));    // 挂绳梯的铁杆
        }

        static void CableLadder(Canvas c, System.Random rng, int u0, int lw)
        {
            Color steel = C(84, 86, 92), cable = C(24, 24, 28), led = Sprites.Hex("ff4fd8");
            int vTop = Hpx + 18;
            Rails(c, u0, lw, 3, vTop, (k, v) => ((v + k * 3) % 10 < 3) ? cable : steel);
            for (int v = 14; v < vTop - 6; v += 20)
                for (int u = u0 + 5; u < u0 + lw - 5; u++) { F(c, u, v, steel); F(c, u, v - 1, C(26, 26, 30)); }
            for (int v = 40; v < Hpx; v += 55 + rng.Next(30))                                // 缠在梯子上的小灯
            {
                int ru = rng.Next(2) == 0 ? u0 + 1 : u0 + lw - 2;
                F(c, ru, v, C(90, 30, 70)); FG(c, ru, v, led);
            }
        }

        static void CageLadder(Canvas c, System.Random rng, int u0, int lw)
        {
            Color steel = C(74, 76, 80), rust = C(128, 70, 40);
            int vTop = Hpx + 22;
            Rails(c, u0, lw, 4, vTop, (k, v) => Mathf.PerlinNoise(k * 0.2f, v * 0.05f) > 0.68f ? rust : steel);
            for (int v = 12; v < vTop - 4; v += 17)
                for (int u = u0 + 6; u < u0 + lw - 6; u++) { F(c, u, v, steel); F(c, u, v + 1, Mul(steel, 0.8f)); F(c, u, v - 1, C(22, 22, 24)); }
            // 护笼：从 3 米往上，每隔一段一道往外拱的圈，三根竖杆连起来
            for (int v = 90; v < vTop; v += 38)
                for (int u = u0 - 6; u <= u0 + lw + 6; u++)
                {
                    float t = (u - (u0 - 6)) / (float)(lw + 12);
                    F(c, u, v - Mathf.Sin(t * Mathf.PI) * 7f, steel);
                    F(c, u, v - Mathf.Sin(t * Mathf.PI) * 7f - 1, C(30, 30, 32));
                }
            foreach (int bu in new[] { u0 - 6, u0 + lw / 2, u0 + lw + 6 })
                for (int v = 90; v < vTop; v++) F(c, bu, v - (bu == u0 + lw / 2 ? 7 : 0), Mul(steel, 0.85f));
            // 顶上一盏红色警示灯
            for (int du = 0; du < 6; du++) for (int dv = 0; dv < 5; dv++) { F(c, u0 + lw / 2 - 3 + du, vTop + 2 + dv, C(90, 20, 20)); FG(c, u0 + lw / 2 - 3 + du, vTop + 2 + dv, Sprites.Hex("ff3a3a")); }
        }
    }

    /// <summary>发光层的闪烁：霓虹偶尔一暗，屏幕雪花抖动，红灯一亮一灭。</summary>
    public class GlowFlicker : MonoBehaviour
    {
        public SpriteRenderer sr; public UnityEngine.Rendering.Universal.Light2D light2d; public float baseLight; public bool blink;
        float t;
        void Update()
        {
            t += Time.deltaTime;
            float k = blink ? (Mathf.Repeat(t, 1.2f) < 0.6f ? 1f : 0.15f) : (Random.value < 0.04f ? 0.3f : 0.85f + Random.value * 0.15f);
            if (sr != null) { var c = sr.color; c.a = k; sr.color = c; }
            if (light2d != null) light2d.intensity = baseLight * k;
        }
    }
}
