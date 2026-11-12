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
        static readonly Color SlabCol = Sprites.Hex("141a22");
        static readonly Color PipeCol = Sprites.Hex("8a4a24");
        static readonly Color PipeHi = Sprites.Hex("b06a3a");
        static readonly Color Water = new Color(0.37f, 0.50f, 0.42f, 0.45f);

        public static void Edge(Transform parent, Vector2 a, Vector2 b, System.Random rng)
        {
            float len = (b - a).magnitude;
            if (len < 0.01f) return;
            Vector2 dir = (b - a) / len;
            bool alongX = Mathf.Abs(a.y - b.y) < 0.001f;
            Vector2 inward = alongX ? new Vector2(0f, -0.35f) : new Vector2(-0.35f, 0f);

            Iso.Face(parent, "Slab", a, b, Slab, SlabCol, -30010, -Slab, glow: true);
            Iso.Face(parent, "SlabLip", a, b, 0.05f, Sprites.Hex("26303c"), -30009, -0.05f, glow: true);
            Iso.Face(parent, "Pipe", a, b, 0.09f, PipeCol, -30005, -0.2f, glow: true);
            Iso.Face(parent, "PipeHi", a, b, 0.025f, PipeHi, -30004, -0.12f, glow: true);

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

            // 出水口：每隔 4–6 格一个，废水成片落到下面
            for (float t = 1.5f + (float)rng.NextDouble() * 2f; t < len - 0.8f; t += 4f + (float)rng.NextDouble() * 2f)
            {
                Vector2 tile = a + dir * t;
                Vector2 p = Iso.W(tile.x, tile.y) + Vector2.down * (0.16f * Iso.S);
                var mouth = Iso.Poly(parent, "Outlet", new[] { p + new Vector2(-0.12f, -0.06f), p + new Vector2(0.12f, -0.06f), p + new Vector2(0.12f, 0.06f), p + new Vector2(-0.12f, 0.06f) },
                                     Sprites.Hex("060a0a"), -30003);
                Drain(parent, p + Vector2.down * 0.05f);
            }
        }

        static readonly Color FenceCol = Sprites.Hex("6a4028");
        static readonly Color FenceDark = Sprites.Hex("3e2618");

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
                Iso.Face(parent, "FenceLow", p, q, 3 * Px, FenceDark, order, 10 * Px, glow: true);
                Iso.Face(parent, "FenceTop", p, q, 4 * Px, FenceCol, order + 1, 20 * Px + sag, glow: true);
                Iso.Face(parent, "FencePost", p, Vector2.Lerp(p, q, 0.08f), 24 * Px, FenceDark, order + 2, 0f, glow: true);
            }
        }

        static void Drain(Transform parent, Vector2 pos)
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
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startColor = Water;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 70f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(0.18f, 0.02f, 0.02f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
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
            r.sharedMaterial = Sprites.Unlit;
            r.sortingOrder = -30002;
            ps.Play();
        }
    }
}
