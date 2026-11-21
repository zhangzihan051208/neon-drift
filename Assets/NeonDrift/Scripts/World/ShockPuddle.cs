using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonDrift
{
    /// <summary>
    /// 积水区的漏电水坑：一个倒在路边的变电箱，断掉的电缆泡在水坑里。
    /// 循环：平静（电缆头偶尔冒一点火花）→ 预警（水面零星噼啪、箱子上的灯闪）→ 带电（水面爬满细小的电弧，站在水里的人和敌人都会被电）。
    /// 电光用暖白色，不用蓝青色。伤害类型是电击，「绝缘层」可以减免。
    /// </summary>
    public class ShockPuddle : MonoBehaviour, IHackable
    {
        enum State { Idle, Warn, Live }

        static readonly Color ArcCol = Sprites.Hex("fff0b0");        // 暖白色电光
        static readonly Color WarnCol = new Color(1f, 0.85f, 0.5f, 0.75f);
        static bool told;                                             // 第一次被电时提示一次
        public static readonly List<ShockPuddle> All = new List<ShockPuddle>();
        Vector2 boxPoint;
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // 骇入变电箱：水坑立刻带电 2 秒
        public Vector2 HackPoint => boxPoint;
        public int HackCost => 1;
        public bool CanHack => st != State.Live;
        public string HackName => "Overload Transformer";
        public void OnHacked(PlayerController p) { ForceLive(2f); GreyboxHUD.Message("Transformer overloaded: the puddle is live", 1.2f); }

        /// <summary>马上带电 seconds 秒（骇入变电箱、霓姬「导电」）。</summary>
        public void ForceLive(float seconds)
        {
            t = st == State.Live ? Mathf.Max(t, seconds) : seconds; st = State.Live; emit = 0f;
            nextHit.Clear();
            CameraRig.Shake(0.04f);
            Sfx.Play("shock_live", 1f, c);
        }

        /// <summary>霓姬「导电」：电击打中站在水坑里的敌人时，整片积水带电 1.5 秒。</summary>
        public static void Conduct(Vector2 feet)
        {
            var p = GameRoot.I?.Player;
            if (p == null || p.N("n_ground") == 0) return;
            foreach (var sp in All) if (sp != null && sp.st != State.Live && sp.Inside(feet)) sp.ForceLive(1.5f);
        }

        Vector2 c; float rx, ry;
        Vector2 cableEnd;
        State st; float t, emit;
        SpriteRenderer sheen, warnLamp;
        Light2D glow;
        readonly Dictionary<Health, float> nextHit = new Dictionary<Health, float>();
        System.Random rng;

        public const float IdleMin = 3f, IdleMax = 5f, WarnTime = 0.9f, LiveTime = 1.1f, Tick = 0.5f, PlayerDamage = 1f, EnemyDamage = 1.5f;

        /// <summary>tile：水坑中心（格子坐标）；boxTile：变电箱所在格（左上角）。</summary>
        public static ShockPuddle Build(Transform parent, Vector2 tile, Vector2 boxTile, System.Random rng)
        {
            var go = new GameObject("漏电水坑");
            go.transform.SetParent(parent, false);
            var sp = go.AddComponent<ShockPuddle>();
            sp.rng = new System.Random(rng.Next());
            sp.c = Iso.W(tile.x, tile.y);
            go.transform.position = sp.c;

            // 水坑：用积水贴花放大，没有贴图时画一个深色椭圆
            float w, h;
            var e = Props.Find("decals", "puddle1");
            if (e != null)
            {
                float k = 1.35f;
                var sr = new GameObject("Water").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(go.transform, false);
                sr.sprite = e.sprite;
                sr.sharedMaterial = Sprites.Unlit;
                sr.sortingOrder = -29984;
                sr.flipX = rng.Next(2) == 0;
                sr.transform.localScale = new Vector3(e.scale * k, e.scale * k, 1f);
                w = e.sprite.bounds.size.x * e.scale * k; h = e.sprite.bounds.size.y * e.scale * k;
            }
            else
            {
                w = 4.4f; h = 2.2f;
                var sr = Sprites.Make(go.transform, "Water", Sprites.Circle(64), Sprites.Hex("0d1424"), -29984, Vector2.zero);
                sr.transform.localScale = new Vector3(w / (64f / Sprites.PPU), h / (64f / Sprites.PPU), 1f);
            }
            sp.rx = w * 0.42f; sp.ry = h * 0.38f;

            // （原来这里有一整片水面反光，加法发光叠上泛光后变成一大块白色椭圆，太刺眼，去掉了：只留水面上的小电弧和一点暖光）

            // 变电箱 + 箱顶的警示灯
            // 变电箱：用 StreamingAssets/Props/hazards 里画好的三张破配电柜（门没关严、弹孔、烧焦、破海报、缠胶布的电缆），
            // 和其他物件一样放（同一套前后遮挡、碰撞、影子，游戏里放大 1.2 倍）；素材缺了才退回代码画的那个
            Vector2 boxTop;
            var cabE = Props.Find("hazards", "transformer" + (1 + rng.Next(3)));
            if (cabE != null)
            {
                var box = Props.Place(go.transform, cabE, new Vector2(boxTile.x + 1.02f, boxTile.y + 0.66f), false, true);
                boxTop = Iso.W(boxTile.x + 0.51f, boxTile.y + 0.33f) + Vector2.up * 1.74f;
                sp.warnLamp = Sprites.Make(box.transform, "Lamp", Sprites.Box(2, 2), Sprites.Hex("ffb030"), 30, boxTop + new Vector2(0.12f, -0.06f) - (Vector2)box.transform.position);
                sp.warnLamp.sharedMaterial = Glow.Mat();
            }
            else Cabinet.Build(go.transform, boxTile, rng, out boxTop, out sp.warnLamp);
            sp.boxPoint = boxTop;

            // 电缆：从箱顶垂下来，在地上拖一段，断头泡在水坑边上
            Vector2 toward = (sp.c - boxTop).normalized;
            sp.cableEnd = sp.c - new Vector2(toward.x * sp.rx * 0.45f, toward.y * sp.ry * 0.45f);
            Vector2 ground = Iso.W(boxTile.x + 1.1f, boxTile.y + 0.9f);
            var pts = new List<Vector2> { boxTop };
            for (int i = 1; i <= 4; i++) { float u = i / 4f; pts.Add(Vector2.Lerp(boxTop, ground, u) + Vector2.down * Mathf.Sin(u * Mathf.PI) * 0.15f); }
            for (int i = 1; i <= 5; i++) { float u = i / 5f; pts.Add(Vector2.Lerp(ground, sp.cableEnd, u) + Vector2.Perpendicular(toward) * Mathf.Sin(u * Mathf.PI * 1.5f) * 0.12f); }
            Color cable = Sprites.Hex("15161a");
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], n = Vector2.Perpendicular(b - a).normalized * 0.035f;
                int ord = i < 4 ? Iso.Order(Mathf.Min(a.y, b.y)) - 1 : -29981;      // 垂下来那段和箱子一起排前后，地上那段贴着地面
                Iso.Poly(go.transform, "Cable", new[] { a - n, b - n, b + n, a + n }, cable, ord);
            }
            Iso.Poly(go.transform, "CableTip", new[] { sp.cableEnd + new Vector2(-0.06f, -0.04f), sp.cableEnd + new Vector2(0.06f, -0.04f), sp.cableEnd + new Vector2(0.06f, 0.04f), sp.cableEnd + new Vector2(-0.06f, 0.04f) }, Sprites.Hex("8a5a30"), -29980);

            sp.glow = Atmosphere.Lamp(go.transform, sp.c + Vector2.up * 0.2f, ArcCol, Mathf.Max(sp.rx, sp.ry) * 1.1f, 0f);
            sp.st = State.Idle;
            sp.t = IdleMin + (float)sp.rng.NextDouble() * (IdleMax - IdleMin);
            return sp;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t -= dt; emit -= dt;
            switch (st)
            {
                case State.Idle:
                    SetGlow(0f, 0f);
                    warnLamp.color = Sprites.Hex("5a3a10");
                    if (emit <= 0f) { emit = 0.9f + (float)rng.NextDouble() * 0.8f; Effects.Sparks(cableEnd, ArcCol, 1, 1.2f); }
                    if (t <= 0f) { st = State.Warn; t = WarnTime; Sfx.Play("shock_warn", 1f, c); }
                    break;
                case State.Warn:
                {
                    float f = Random.value;
                    SetGlow(0.04f + f * 0.08f, 0.15f * f);
                    warnLamp.color = Mathf.Repeat(Time.time * 8f, 1f) < 0.5f ? Sprites.Hex("ffb030") : Sprites.Hex("5a3a10");
                    if (emit <= 0f)
                    {
                        emit = 0.16f;
                        Arc(WarnCol, near: true);
                        if (Random.value < 0.5f) Effects.Sparks(cableEnd, ArcCol, 2, 2f);
                    }
                    if (t <= 0f) { st = State.Live; t = LiveTime; CameraRig.Shake(0.03f); Sfx.Play("shock_live", 1f, c); }
                    break;
                }
                case State.Live:
                {
                    float f = Random.value;
                    SetGlow(0.16f + f * 0.16f, 0.35f + f * 0.35f);
                    warnLamp.color = Sprites.Hex("ffe08a");
                    if (emit <= 0f)
                    {
                        emit = 0.08f;
                        Arc(ArcCol, near: false);
                        if (Random.value < 0.25f) Arc(ArcCol, near: false);
                        if (Random.value < 0.3f) Effects.Sparks(cableEnd, ArcCol, 2, 2.5f);
                    }
                    Shock();
                    if (t <= 0f) { st = State.Idle; t = IdleMin + (float)rng.NextDouble() * (IdleMax - IdleMin); SetGlow(0f, 0f); nextHit.Clear(); }
                    break;
                }
            }
        }

        void SetGlow(float sheenA, float light)
        {
            if (glow != null) glow.intensity = light * 0.4f;             // 只是一点点暖光，跟着电弧闪
        }

        /// <summary>在水面上随机一点画一道小电弧；预警时只出现在电缆头附近。</summary>
        void Arc(Color col, bool near)
        {
            Vector2 p;
            if (near) p = cableEnd + Random.insideUnitCircle * 0.5f;
            else
            {
                float a = Random.value * Mathf.PI * 2f, r = Mathf.Sqrt(Random.value);
                p = c + new Vector2(Mathf.Cos(a) * rx * r, Mathf.Sin(a) * ry * r);
            }
            PixelFx.Play(PixelFx.PuddleArc(Random.Range(0, 6)), p, Random.Range(-25f, 25f), col, 22f, -29982, Random.value < 0.5f);
        }

        public bool Inside(Vector2 feet)
        {
            Vector2 d = feet - c;
            return (d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry) < 1f;
        }

        /// <summary>带电时：水里的玩家和敌人每 0.5 秒挨一下电。</summary>
        void Shock()
        {
            var p = GameRoot.I?.Player;
            if (p != null && !p.IsDead && Inside(p.transform.position)) Hit(p.Hp, PlayerDamage, p.transform.position, true);
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || !Inside(e.transform.position)) continue;
                var h = e.GetComponent<Health>();
                if (h != null) Hit(h, EnemyDamage, e.transform.position, false);
            }
        }

        void Hit(Health h, float dmg, Vector2 at, bool player)
        {
            if (nextHit.TryGetValue(h, out var tn) && Time.time < tn) return;
            nextHit[h] = Time.time + Tick;
            Vector2 away = at - c; if (away.sqrMagnitude < 0.01f) away = Vector2.down;
            if (h.Damage(dmg, away.normalized, DamageKind.Shock))
            {
                PixelFx.Play(PixelFx.Zap(6), at + Vector2.up * 0.9f, 0f, ArcCol, 24f);
                Effects.Flash(at + Vector2.up * 0.5f, ArcCol, 2.5f, 1.2f, 0.08f);
                if (player && !told)
                {
                    told = true;
                    GreyboxHUD.Message("Live water! Stay out when it sparks and the box light blinks", 3f);
                }
            }
        }
    }

    /// <summary>
    /// 变电箱：路边的配电柜，逐像素画成和场景一样的夜景风格——深色旧钢板、往下流的锈、墙根的泥、
    /// 被路灯打亮的一道暖色顶边、雨水的反光点、柜门缝、百叶散热口、黄色高压警示贴、一小块乱涂的标记。
    /// 光是画在图里的（和房子、物件一样用不受灯光影响的材质）；顶上一颗琥珀色指示灯单独发光，预警时闪。
    /// 占地 0.9 × 0.6 格，高 1.5 单位，带碰撞。
    /// </summary>
    public static class Cabinet
    {
        const float A = 0.9f, B = 0.6f, H = 1.5f;      // 沿 x、沿 y 的占地（格），高度（单位）
        const int PPU = Sprites.PPU;

        static Color C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static Color Mul(Color c, float k) { c.r *= k; c.g *= k; c.b *= k; c.a = 1f; return c; }
        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            return Mathf.Sqrt((px - ax - vx * t) * (px - ax - vx * t) + (py - ay - vy * t) * (py - ay - vy * t));
        }
        static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }

        public static GameObject Build(Transform parent, Vector2 tile, System.Random rng, out Vector2 topCenter, out SpriteRenderer lamp)
        {
            int seed = rng.Next(100000);
            int ax = Mathf.RoundToInt(A * PPU), bx = Mathf.RoundToInt(B * PPU), hp = Mathf.RoundToInt(H * PPU);
            int ay = ax / 2, by = bx / 2;
            int w = ax + bx + 4, h = ay + by + hp + 4;
            int fx = ax + 2, fy = 2;                                                          // 图上的前角（支点）
            var px = new Color32[w * h];
            var col = new Color[w * h];
            var has = new bool[w * h];

            Color paintL = C(52, 60, 54), paintR = C(36, 42, 40), paintT = C(66, 72, 64);    // 发灰的旧墨绿漆：左面朝路灯稍亮，右面背光
            Color rust = C(98, 54, 32), rustDark = C(62, 36, 24), metal = C(84, 84, 80), outline = C(16, 17, 16);
            Color rim = C(176, 112, 64);                                                       // 路灯打在顶边的暖光

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x - fx, dy = y - fy;
                    Color c; bool ok = false; c = default;
                    // 顶面：(dx, dy) = (0, hp) + (−32s, 16s) + (32t, 16t)
                    float ss = ((dy - hp) / 16f - dx / 32f) / 2f, tt = ((dy - hp) / 16f + dx / 32f) / 2f;
                    if (ss >= 0 && ss <= A && tt >= 0 && tt <= B)
                    {
                        ok = true;
                        c = Mul(paintT, 0.9f + Hash(x, y, seed) * 0.15f);
                        if (Mathf.PerlinNoise(x * 0.2f + seed, y * 0.2f) > 0.62f) c = Color.Lerp(c, rust, 0.6f);
                        if (ss < 0.06f || tt < 0.06f) c = Color.Lerp(c, rim, 0.42f);                   // 前沿两道被灯打亮
                        if (Hash(x, y, seed + 3) > 0.985f) c = C(150, 150, 140);                      // 雨水反光点
                    }
                    else if (dx <= 0)
                    {
                        // 左面（沿 x 的那一面）：(dx, dy) = (−32s, 16s) + (0, v)
                        float s1 = -dx / 32f, v = dy - 16f * s1;
                        if (s1 <= A && v >= 0 && v < hp)
                        {
                            ok = true;
                            float u = s1 / A, vv = v / hp;
                            c = Mul(paintL, 0.9f + Hash(x, y, seed) * 0.14f);
                            // 柜门：四周一圈门缝，右上角一个门锁，左边两个合页
                            bool seam = (Mathf.Abs(u - 0.1f) < 0.02f || Mathf.Abs(u - 0.9f) < 0.02f) && vv > 0.12f && vv < 0.9f
                                     || (Mathf.Abs(vv - 0.12f) < 0.015f || Mathf.Abs(vv - 0.9f) < 0.015f) && u > 0.1f && u < 0.9f;
                            if (seam) c = Mul(c, 0.55f);
                            if (Mathf.Abs(u - 0.82f) < 0.03f && Mathf.Abs(vv - 0.55f) < 0.03f) c = metal;
                            if (Mathf.Abs(u - 0.14f) < 0.025f && (Mathf.Abs(vv - 0.3f) < 0.02f || Mathf.Abs(vv - 0.75f) < 0.02f)) c = metal;
                            // 黄色高压警示贴：三角形 + 黑色闪电
                            float tu = (u - 0.33f) / 0.34f, tv = (vv - 0.42f) / 0.3f;
                            if (tv >= 0 && tv <= 1 && Mathf.Abs(tu - 0.5f) <= (1f - tv) * 0.5f + 0.02f)
                            {
                                bool edge = Mathf.Abs(tu - 0.5f) > (1f - tv) * 0.5f - 0.09f || tv < 0.09f;          // 黑边
                                c = edge ? C(30, 26, 18) : Hash(x, y, seed + 7) < 0.1f ? C(150, 120, 40) : C(190, 150, 44);
                                float d = Mathf.Min(Seg(tu, tv * 0.9f, 0.56f, 0.62f, 0.44f, 0.36f),
                                          Mathf.Min(Seg(tu, tv * 0.9f, 0.44f, 0.36f, 0.58f, 0.36f), Seg(tu, tv * 0.9f, 0.58f, 0.36f, 0.46f, 0.12f)));
                                if (d < 0.045f && !edge) c = C(24, 22, 18);                                            // 黑色闪电
                            }
                            // 锈：从上往下流的几道 + 底部一圈
                            if (Mathf.PerlinNoise(x * 0.6f + seed, vv * 3f) > 0.7f && Hash(x, 0, seed) > 0.5f) c = Color.Lerp(c, rust, 0.55f);
                            if (vv < 0.1f) c = Color.Lerp(c, rustDark, 0.5f + (0.1f - vv) * 3f);
                            if (Hash(x, y, seed + 11) > 0.985f) c = metal;                               // 掉漆
                            if (s1 < 0.05f) c = Color.Lerp(c, rim, 0.35f);                               // 前棱被灯打亮
                        }
                    }
                    else
                    {
                        // 右面（沿 y 的那一面）：(dx, dy) = (32t, 16t) + (0, v)
                        float t1 = dx / 32f, v = dy - 16f * t1;
                        if (t1 <= B && v >= 0 && v < hp)
                        {
                            ok = true;
                            float u = t1 / B, vv = v / hp;
                            c = Mul(paintR, 0.9f + Hash(x, y, seed) * 0.14f);
                            // 百叶散热口：上半部分几道深色横条
                            if (vv > 0.55f && vv < 0.85f && u > 0.2f && u < 0.8f && ((int)v % 4) < 1) c = C(18, 20, 20);
                            // 底部电缆出口
                            if (u > 0.55f && u < 0.75f && vv > 0.05f && vv < 0.16f) c = C(12, 12, 12);
                            // 一块乱涂的洋红色标记
                            if (u > 0.15f && u < 0.45f && vv > 0.25f && vv < 0.42f && Mathf.Abs(Mathf.Sin(u * 30f) * 0.04f + 0.33f - vv) < 0.025f) c = C(110, 46, 92);
                            if (Mathf.PerlinNoise(x * 0.5f, vv * 4f + seed) > 0.68f) c = Color.Lerp(c, rustDark, 0.6f);
                            if (vv < 0.1f) c = Color.Lerp(c, rustDark, 0.5f + (0.1f - vv) * 3f);
                        }
                    }
                    if (ok) { col[y * w + x] = c; has[y * w + x] = true; }
                }
            // 1 像素深色描边 + 写回
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (has[i]) { px[i] = col[i]; continue; }
                    bool edge = (x > 0 && has[i - 1]) || (x < w - 1 && has[i + 1]) || (y > 0 && has[i - w]) || (y < h - 1 && has[i + w]);
                    if (edge) px[i] = outline;
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Cabinet" };
            tex.SetPixels32(px); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(fx / (float)w, fy / (float)h), PPU, 0, SpriteMeshType.FullRect);

            Vector2 front = Iso.W(tile.x + A, tile.y + B);
            var root = new GameObject("变电箱");
            root.transform.SetParent(parent, true);
            root.transform.position = front;
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = Sprites.Unlit;                                                     // 和房子、物件一样：光画在图里
            var poly = root.AddComponent<PolygonCollider2D>();
            poly.points = new[] { Iso.W(tile.x, tile.y) - front, Iso.W(tile.x + A, tile.y) - front, Vector2.zero, Iso.W(tile.x, tile.y + B) - front };
            root.AddComponent<UnityEngine.Rendering.SortingGroup>();
            root.AddComponent<YSort>().isStatic = true;

            topCenter = Iso.W(tile.x + A * 0.5f, tile.y + B * 0.5f) + Vector2.up * H;
            // 顶上的琥珀色指示灯（发光，预警时由 ShockPuddle 控制闪烁）
            lamp = Sprites.Make(root.transform, "Lamp", Sprites.Box(2, 2), Sprites.Hex("ffb030"), 3, (topCenter + new Vector2(-0.12f, 0.02f)) - front);
            lamp.sharedMaterial = Glow.Mat();
            return root;
        }
    }
}
