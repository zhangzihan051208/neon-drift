using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 平台的前沿（朝镜头的两条边）：混凝土厚板侧面、沿边的锈水管和出水口落水、下面一排桩子。
    /// 只用于 y = 常数 或 x = 常数 的边，并且是街区/街道的前侧（y 最大或 x 最大的那条）。
    /// </summary>
    public static class Platform
    {
        const float Slab = 0.6f;             // 厚板高度（格子竖直单位，会乘 Iso.S）
        static readonly Color SlabCol = Sprites.Hex("0b0c0f");      // 平台边缘：近乎纯黑
        static readonly Color PipeCol = Sprites.Hex("8a4a24");
        static readonly Color PipeHi = Sprites.Hex("b06a3a");
        static readonly Color Water = new Color(0.30f, 0.40f, 0.36f, 0.34f);

        public static void Edge(Transform parent, Vector2 a, Vector2 b, System.Random rng)
        {
            float len = (b - a).magnitude;
            if (len < 0.01f) return;
            Vector2 dir = (b - a) / len;
            bool alongX = Mathf.Abs(a.y - b.y) < 0.001f;
            Vector2 inward = alongX ? new Vector2(0f, -0.35f) : new Vector2(-0.35f, 0f);

            Iso.Face(parent, "Slab", a, b, Slab, SlabCol, -30010, -Slab, glow: true);
            Iso.Face(parent, "SlabLip", a, b, 0.05f, Sprites.Hex("1c1e23"), -30009, -0.05f, glow: true);
            SlabStreaks(parent, a, dir, len, rng);
            RustyRun(parent, a, dir, len, rng);

            var pile = Props.Find("platform", "pile");
            if (pile != null)
            {
                for (float t = 0.8f; t < len - 0.3f; t += 3f)
                {
                    Vector2 tile = a + dir * t + inward;
                    var sr = new GameObject("Pile").AddComponent<SpriteRenderer>();
                    sr.transform.SetParent(parent, true);
                    sr.transform.position = Iso.W(tile.x, tile.y) + Vector2.down * (Slab * Iso.S * 0.8f);
                    sr.sprite = pile.sprite;
                    sr.flipX = rng.Next(2) == 0;
                    sr.sharedMaterial = Sprites.Unlit;
                    sr.sortingOrder = -30020;
                }
            }

            // 排水管：每隔 4–6 格从沿边的横管上接一根竖管下来，末端一个朝外的弯头，废水从弯头口落下去
            for (float t = 1.5f + (float)rng.NextDouble() * 2f; t < len - 0.8f; t += 4f + (float)rng.NextDouble() * 2f)
            {
                Vector2 tile = a + dir * t;
                Vector2 top = Snap(Iso.W(tile.x, tile.y) + Vector2.down * (0.13f * Iso.S));
                bool big = rng.NextDouble() < 0.3;
                float drop = Slab * Iso.S * (0.75f + (float)rng.NextDouble() * 0.9f);     // 有的只到板底，有的再往下伸一截
                Downpipe(parent, top, Mathf.Round(drop * 32f) / 32f, big ? 4 : 3, big);
            }
        }

        // ---------------------------------------------------------------- 做旧：锈迹、深浅不一的管子
        static Color Jit(Color c, System.Random rng, float amt)
        {
            float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * amt;
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }

        static readonly Color[] RustTones = { Sprites.Hex("7a3e1f"), Sprites.Hex("8a4a24"), Sprites.Hex("6b3a22"), Sprites.Hex("94532a"), Sprites.Hex("5c3020") };
        static readonly Color RustBloom = Sprites.Hex("b8642c"), RustDark = Sprites.Hex("3a1e10");

        /// <summary>沿边的横水管：一段一段深浅不一，夹着亮锈斑和深色接口箍，高光有的地方锈断了。</summary>
        static void RustyRun(Transform parent, Vector2 a, Vector2 dir, float len, System.Random rng)
        {
            float t = 0f, nextRing = 0.6f + (float)rng.NextDouble();
            while (t < len)
            {
                float seg = Mathf.Min(len - t, 0.2f + (float)rng.NextDouble() * 0.6f);
                Vector2 p = a + dir * t, q = a + dir * (t + seg);
                var c = Jit(RustTones[rng.Next(RustTones.Length)], rng, 0.08f);
                Iso.Face(parent, "Pipe", p, q, 0.09f, c, -30005, -0.2f, glow: true);
                if (rng.NextDouble() > 0.2)                                              // 高光，五段里约有一段锈断
                    Iso.Face(parent, "PipeHi", p, q, 0.025f, Jit(PipeHi, rng, 0.12f), -30004, -0.12f, glow: true);
                if (rng.NextDouble() < 0.35)                                             // 亮锈斑
                {
                    float s0 = t + (float)rng.NextDouble() * seg * 0.7f, sl = 0.04f + (float)rng.NextDouble() * 0.1f;
                    Iso.Face(parent, "PipeRust", a + dir * s0, a + dir * Mathf.Min(len, s0 + sl), 0.05f, Jit(RustBloom, rng, 0.1f), -30004, -0.19f + (float)rng.NextDouble() * 0.04f, glow: true);
                }
                t += seg;
                if (t >= nextRing && t < len - 0.1f)                                     // 接口箍：比管子粗一圈、更暗
                {
                    Iso.Face(parent, "PipeRing", a + dir * t, a + dir * Mathf.Min(len, t + 0.06f), 0.13f, RustDark, -30003, -0.22f, glow: true);
                    nextRing = t + 1.2f + (float)rng.NextDouble() * 1.2f;
                }
            }
        }

        /// <summary>黑色平台边缘上从水管往下流的锈迹：上面浓、往下渐淡，宽窄长短不一。</summary>
        static void SlabStreaks(Transform parent, Vector2 a, Vector2 dir, float len, System.Random rng)
        {
            for (float t = 0.2f + (float)rng.NextDouble() * 0.5f; t < len - 0.1f; t += 0.35f + (float)rng.NextDouble() * 0.9f)
            {
                float w = 0.03f + (float)rng.NextDouble() * 0.08f;
                float h = (Slab - 0.2f) * (0.3f + (float)rng.NextDouble() * 0.65f);      // 从水管下沿往下，不超过平台底边
                var mr = Iso.Face(parent, "RustStreak", a + dir * t, a + dir * Mathf.Min(len, t + w), h, Color.white, -30008, -0.2f - h + 0.02f, glow: true);
                var top = Jit(rng.NextDouble() < 0.7 ? Sprites.Hex("5a2c16") : Sprites.Hex("3e3226"), rng, 0.15f); top.a = 0.75f;
                var bot = top; bot.a = 0f;
                var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                mesh.colors = new[] { bot, bot, top, top };                             // 顶点顺序：下 a、下 b、上 b、上 a
            }
        }

        const float P = 1f / 32f;                                                           // 1 个像素
        static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x * 32f) / 32f, Mathf.Round(v.y * 32f) / 32f);

        static void Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color c, int order)
        {
            Iso.Poly(parent, name, new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) }, c, order, glow: true);
        }

        static readonly Color PipeDark = Sprites.Hex("532a14");
        static readonly Color PipeRing = Sprites.Hex("6e3a1c");

        /// <summary>一根竖直的锈水管：top 是接横管的位置，h 是长度，r 是半宽（像素）。</summary>
        static void Downpipe(Transform parent, Vector2 top, float h, int r, bool big)
        {
            float x = top.x, y1 = top.y, y0 = top.y - h;
            var rr = new System.Random(Mathf.RoundToInt(x * 977 + y1 * 131));
            Box(parent, "PipeV", x - r * P, y0, x + r * P, y1, Jit(RustTones[rr.Next(RustTones.Length)], rr, 0.08f), -30004);
            for (float yy = y1 - 0.2f; yy > y0 + 0.1f; yy -= 0.25f + (float)rr.NextDouble() * 0.4f)      // 管身上的锈斑
                if (rr.NextDouble() < 0.5) Box(parent, "PipeVRust", x - (r - 1) * P, yy - (1 + rr.Next(3)) * P, x + (rr.Next(2) == 0 ? r - 1 : r - 2) * P, yy, Jit(RustBloom, rr, 0.12f), -30003);
            Box(parent, "PipeVHi", x - (r - 1) * P, y0, x - (r - 2) * P, y1, PipeHi, -30003);          // 左侧一条高光
            Box(parent, "PipeVShade", x + (r - 2) * P, y0, x + r * P, y1, PipeDark, -30003);            // 右侧背光
            Box(parent, "PipeJoint", x - (r + 1) * P, y1 - 2 * P, x + (r + 1) * P, y1 + 2 * P, PipeRing, -30002);   // 和横管的接头
            for (float y = y1 - 0.5f; y > y0 + 0.3f; y -= 0.55f)                                      // 管箍
                Box(parent, "PipeBand", x - (r + 1) * P, y, x + (r + 1) * P, y + 2 * P, PipeRing, -30002);
            // 弯头：比管身粗一圈，中间一个黑洞洞的口子朝着镜头
            int e = r + 2;
            float ey1 = y0 + 2 * P, ey0 = ey1 - (2 * e - 1) * P;
            Box(parent, "Elbow", x - e * P, ey0, x + e * P, ey1, PipeRing, -30002);
            Box(parent, "ElbowHi", x - e * P, ey1 - P, x + e * P, ey1, PipeHi, -30001);
            Box(parent, "Mouth", x - (e - 2) * P, ey0 + P, x + (e - 2) * P, ey1 - 2 * P, Sprites.Hex("060a0a"), -30001);
            Box(parent, "Slime", x - (e - 2) * P, ey0 - 3 * P, x - (e - 3) * P, ey0, new Color(0.22f, 0.30f, 0.20f, 1f), -30002);     // 口子下面挂着的一点苔
            Drain(parent, new Vector2(x, ey0 - P), (e - 2) * 2 * P, big);
        }

        static readonly Color FenceCol = Sprites.Hex("6a4028");
        static readonly Color FenceDark = Sprites.Hex("3e2618");
        static readonly Color[] FenceTones = { Sprites.Hex("6a4028"), Sprites.Hex("5e3a26"), Sprites.Hex("74462a"), Sprites.Hex("4f3424"), Sprites.Hex("7a4a2c") };

        /// <summary>
        /// 一段破护栏（只是样子，碰撞另外加）。back = 在房子后面那一圈（排在房子底下），
        /// 否则排在角色前面（朝镜头的那两条边）。gap0–gap1 是沿这条边留空的一段（出口的路），按到 a 的距离算。
        /// </summary>
        public static void Fence(Transform parent, Vector2 a, Vector2 b, bool back, System.Random rng, float gap0 = -1f, float gap1 = -1f)
        {
            float len = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.RoundToInt(len));
            for (int i = 0; i < n; i++)
            {
                float t0 = len * i / n, t1 = len * (i + 1) / n;
                if (gap0 >= 0f && t1 > gap0 && t0 < gap1) continue;     // 只有真的有出口时才留空
                Vector2 p = Vector2.Lerp(a, b, i / (float)n), q = Vector2.Lerp(a, b, (i + 1) / (float)n);
                float lowY = Mathf.Min(Iso.W(p.x, p.y).y, Iso.W(q.x, q.y).y);
                float topY = Mathf.Max(Iso.W(p.x, p.y).y, Iso.W(q.x, q.y).y);
                int order = back ? Buildings.BackLayer - 3500 + Iso.Order(topY + 0.05f) : Iso.Order(lowY - 0.05f);
                double roll = rng.NextDouble();
                const float Px = 1f / 64f;                                     // 高度参数里的 1 像素（会乘 Iso.S，再乘 32 像素/单位）
                float sag = roll > 0.85 ? -5 * Px : 0f;                        // 撞弯的
                // 做旧：每格颜色略有不同，偶尔一格是残留的灰色旧漆；横杆上有锈斑
                Color top = rng.NextDouble() < 0.15 ? Jit(Sprites.Hex("4a4644"), rng, 0.1f) : Jit(FenceTones[rng.Next(FenceTones.Length)], rng, 0.08f);
                Color dark = Jit(FenceDark, rng, 0.15f);
                Iso.Face(parent, "FenceLow", p, q, 3 * Px, dark, order, 10 * Px, glow: true);
                Iso.Face(parent, "FenceTop", p, q, 4 * Px, top, order + 1, 20 * Px + sag, glow: true);
                Iso.Face(parent, "FencePost", p, Vector2.Lerp(p, q, 0.08f), 24 * Px, Jit(FenceDark, rng, 0.2f), order + 2, 0f, glow: true);
                int spots = rng.Next(3);
                for (int k = 0; k < spots; k++)
                {
                    float u0 = 0.1f + (float)rng.NextDouble() * 0.75f, ul = 0.05f + (float)rng.NextDouble() * 0.12f;
                    bool onTop = rng.NextDouble() < 0.7;
                    Iso.Face(parent, "FenceRust", Vector2.Lerp(p, q, u0), Vector2.Lerp(p, q, Mathf.Min(1f, u0 + ul)), (onTop ? 2 : 1) * Px,
                             Jit(rng.NextDouble() < 0.6 ? RustBloom : Sprites.Hex("8e4c24"), rng, 0.12f), order + 3, (onTop ? 21 : 11) * Px + (onTop ? sag : 0f), glow: true);
                }
            }
        }

        static void Drain(Transform parent, Vector2 pos, float width, bool big)
        {
            var go = new GameObject("Drain");
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.55f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, big ? 0.12f : 0.08f);
            main.startColor = Water;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = big ? 90f : 55f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(width, 0.02f, 0.02f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            vel.y = new ParticleSystem.MinMaxCurve(-11f, -13f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            r.sharedMaterial = Sprites.MeshUnlit;
            r.sortingOrder = -30001;
            ps.Play();
        }
    }
}
