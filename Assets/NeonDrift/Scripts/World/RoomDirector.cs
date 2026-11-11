using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    public enum Side { None, NE, NW }   // NE：沿 y 最小的那条边（往右上走）；NW：沿 x 最小的那条边（往左上走）

    /// <summary>
    /// 一个街区：菱形场地 + 后方两面墙 + 前方护栏 + 掩体 + 波次刷怪。
    /// 玩家走进来后入口和出口被闸门封住，清完敌人后出口打开。
    /// </summary>
    public class RoomDirector : MonoBehaviour
    {
        public const int GateWidth = 3;

        public int Wave { get; private set; }
        public int WaveCount { get; private set; }
        public int Alive { get; private set; }
        public bool Cleared { get; private set; }
        public bool Started { get; private set; }
        public int Index { get; private set; }
        public bool IsLast { get; private set; }

        // 街区在全局格子坐标里的范围，以及出入口
        public int bx, by, gx, gy;
        public Side exitSide; public int exitAt;        // 出口在后墙上的起始格
        public Side entrySide; public int entryAt;      // 入口在前沿上的起始格（NE 进来的入口在 y 最大的边，NW 进来的在 x 最大的边）

        PlayerController player;
        GameObject exitGate, entryGate;
        readonly List<Rect> blocked = new List<Rect>();

        static readonly Color FloorCol = Sprites.Hex("1c2024");
        static readonly Color StripeCol = new Color(1f, 1f, 1f, 0.025f);
        static readonly Color WallCol = Sprites.Hex("2e3236");
        static readonly Color WallTop = Sprites.Hex("4a5056");
        static readonly Color RailCol = Sprites.Hex("4a5458");
        static readonly Color WaterCol = Sprites.Hex("0d1424");
        const float WallH = 1.7f;

        public static RoomDirector Build(Transform parent, int index, bool isLast, int bx, int by, int gx, int gy,
                                         Side entrySide, int entryAt, Side exitSide, int exitAt, PlayerController player, System.Random rng)
        {
            var go = new GameObject($"Block {index}");
            go.transform.SetParent(parent, false);
            var rd = go.AddComponent<RoomDirector>();
            rd.Index = index; rd.IsLast = isLast; rd.player = player;
            rd.bx = bx; rd.by = by; rd.gx = gx; rd.gy = gy;
            rd.entrySide = entrySide; rd.entryAt = entryAt; rd.exitSide = exitSide; rd.exitAt = exitAt;
            rd.Generate(rng);
            return rd;
        }

        void Generate(System.Random rng)
        {
            int x0 = bx, y0 = by, x1 = bx + gx, y1 = by + gy;
            var t = transform;

            // 地面 + 条纹（方便看清格子大小）
            Iso.Poly(t, "Floor", new[] { Iso.W(x0, y0), Iso.W(x1, y0), Iso.W(x1, y1), Iso.W(x0, y1) }, FloorCol, -30000);
            for (int j = y0; j < y1; j += 2)
                Iso.Poly(t, "Stripe", new[] { Iso.W(x0, j), Iso.W(x1, j), Iso.W(x1, j + 1), Iso.W(x0, j + 1) }, StripeCol, -29990);
            for (int i = x0; i < x1; i += 2)
                Iso.Poly(t, "Stripe", new[] { Iso.W(i, y0), Iso.W(i + 1, y0), Iso.W(i + 1, y1), Iso.W(i, y1) }, StripeCol, -29990);
            // 人行道
            Iso.Poly(t, "Walk", new[] { Iso.W(x0, y0), Iso.W(x1, y0), Iso.W(x1, y0 + 1.5f), Iso.W(x0, y0 + 1.5f) }, new Color(1, 1, 1, 0.05f), -29980);
            Iso.Poly(t, "Walk", new[] { Iso.W(x0, y0), Iso.W(x0 + 1.5f, y0), Iso.W(x0 + 1.5f, y1), Iso.W(x0, y1) }, new Color(1, 1, 1, 0.05f), -29980);
            // 积水（装饰）
            for (int i = 0; i < 3; i++)
            {
                var c = Iso.W(x0 + 3 + (float)rng.NextDouble() * (gx - 6), y0 + 3 + (float)rng.NextDouble() * (gy - 6));
                var pud = Sprites.Make(t, "Puddle", Sprites.Circle(64), WaterCol, -29970, c);
                float s = 0.8f + (float)rng.NextDouble() * 0.8f;
                pud.transform.localScale = new Vector3(s * 1.41f, s * 0.7f, 1f);
            }

            var bounds = new GameObject("Bounds");
            bounds.transform.SetParent(t, false);

            // 后墙（NE：y = y0；NW：x = x0），出口处留缺口
            for (int i = x0; i < x1; i++)
            {
                if (exitSide == Side.NE && i >= x0 + exitAt && i < x0 + exitAt + GateWidth) continue;
                WallSeg(new Vector2(i, y0), new Vector2(i + 1, y0), (i - x0) % 5 == 2);
            }
            for (int j = y0; j < y1; j++)
            {
                if (exitSide == Side.NW && j >= y0 + exitAt && j < y0 + exitAt + GateWidth) continue;
                WallSeg(new Vector2(x0, j + 1), new Vector2(x0, j), (j - y0) % 5 == 2);
            }
            EdgeWithGap(bounds, new Vector2(x0, y0), new Vector2(x1, y0), exitSide == Side.NE ? exitAt : -1);
            EdgeWithGap(bounds, new Vector2(x0, y0), new Vector2(x0, y1), exitSide == Side.NW ? exitAt : -1);
            // 前沿护栏（y = y1 和 x = x1），入口处留缺口
            EdgeWithGap(bounds, new Vector2(x0, y1), new Vector2(x1, y1), entrySide == Side.NE ? entryAt : -1, rail: true);
            EdgeWithGap(bounds, new Vector2(x1, y0), new Vector2(x1, y1), entrySide == Side.NW ? entryAt : -1, rail: true);

            // 闸门
            if (exitSide == Side.NE) exitGate = Gate(new Vector2(x0 + exitAt, y0), new Vector2(x0 + exitAt + GateWidth, y0), false);
            if (exitSide == Side.NW) exitGate = Gate(new Vector2(x0, y0 + exitAt), new Vector2(x0, y0 + exitAt + GateWidth), false);
            if (entrySide == Side.NE) entryGate = Gate(new Vector2(x0 + entryAt, y1), new Vector2(x0 + entryAt + GateWidth, y1), true);
            if (entrySide == Side.NW) entryGate = Gate(new Vector2(x1, y0 + entryAt), new Vector2(x1, y0 + entryAt + GateWidth), true);
            if (entryGate != null) entryGate.SetActive(false);

            // 出入口前留空，不放掩体
            if (exitSide == Side.NE) blocked.Add(new Rect(x0 + exitAt - 1, y0, GateWidth + 2, 4));
            if (exitSide == Side.NW) blocked.Add(new Rect(x0, y0 + exitAt - 1, 4, GateWidth + 2));
            if (entrySide == Side.NE) blocked.Add(new Rect(x0 + entryAt - 1, y1 - 5, GateWidth + 2, 5));
            if (entrySide == Side.NW) blocked.Add(new Rect(x1 - 5, y0 + entryAt - 1, 5, GateWidth + 2));
            if (entrySide == Side.None) blocked.Add(new Rect(x0, y1 - 6, 6, 6));           // 出生点
            int covers = 3 + rng.Next(3);
            for (int i = 0, tries = 0; i < covers && tries < 200; tries++)
            {
                int w = 1 + rng.Next(2), h = 1 + rng.Next(2);
                int cx = x0 + 2 + rng.Next(Mathf.Max(1, gx - 4 - w)), cy = y0 + 2 + rng.Next(Mathf.Max(1, gy - 4 - h));
                var r = new Rect(cx - 1, cy - 1, w + 2, h + 2);
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                if (hit) continue;
                blocked.Add(r);
                Iso.Box(t, "Cover", cx, cy, cx + w, cy + h, 0.5f + (float)rng.NextDouble() * 0.5f, Sprites.Hex(rng.Next(2) == 0 ? "3a3e42" : "4a3a2a"));
                i++;
            }

            WaveCount = Mathf.Min(4, 2 + (Index - 1) / 2);
        }

        void WallSeg(Vector2 a, Vector2 b, bool lit)
        {
            float topY = Mathf.Max(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y);
            int order = Iso.Order(topY + 0.05f);
            Iso.Face(transform, "Wall", a, b, WallH, WallCol, order);
            Iso.Face(transform, "WallTop", a, b, 0.12f, WallTop, order + 1, WallH - 0.12f);
            if (lit)                                                              // 一扇亮着的窗，帮助判断方向
            {
                Vector2 m0 = Vector2.Lerp(a, b, 0.25f), m1 = Vector2.Lerp(a, b, 0.75f);
                var colors = new[] { "ffd99a", "ff9fd0", "9ad8ff", "c8ffd8" };
                Iso.Face(transform, "Window", m0, m1, 0.5f, Sprites.Hex(colors[(int)(Mathf.Abs(a.x * 3 + a.y * 7)) % colors.Length]), order + 2, 0.75f);
            }
        }

        void EdgeWithGap(GameObject host, Vector2 a, Vector2 b, int gapAt, bool rail = false)
        {
            Vector2 dir = (b - a).normalized;
            float len = (b - a).magnitude;
            if (gapAt < 0) { Iso.Edge(host, a, b); if (rail) Rail(a, b); return; }
            Vector2 g0 = a + dir * gapAt, g1 = a + dir * (gapAt + GateWidth);
            if (gapAt > 0) { Iso.Edge(host, a, g0); if (rail) Rail(a, g0); }
            if (gapAt + GateWidth < len) { Iso.Edge(host, g1, b); if (rail) Rail(g1, b); }
        }

        void Rail(Vector2 a, Vector2 b)
        {
            int n = Mathf.RoundToInt((b - a).magnitude);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)n), q = Vector2.Lerp(a, b, (i + 1) / (float)n);
                float lowY = Mathf.Min(Iso.W(p.x, p.y).y, Iso.W(q.x, q.y).y);
                Iso.Face(transform, "Rail", p, q, 0.06f, RailCol, Iso.Order(lowY - 0.05f), 0.3f);
                Iso.Face(transform, "RailPost", p, Vector2.Lerp(p, q, 0.08f), 0.3f, RailCol, Iso.Order(lowY - 0.05f));
            }
        }

        GameObject Gate(Vector2 a, Vector2 b, bool front)
        {
            var go = new GameObject(front ? "EntryGate" : "ExitGate");
            go.transform.SetParent(transform, false);
            Iso.Edge(go, a, b);
            float y = front ? Mathf.Min(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y) - 0.05f : Mathf.Max(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y) + 0.05f;
            var c = Sprites.Hex("e43b44"); c.a = 0.7f;
            Iso.Face(go.transform, "Shutter", a, b, front ? 0.9f : WallH, c, Iso.Order(y));
            return go;
        }

        public bool Contains(Vector2 world, float inset)
        {
            var tpos = Iso.Tile(world);
            return tpos.x > bx + inset && tpos.x < bx + gx - inset && tpos.y > by + inset && tpos.y < by + gy - inset;
        }

        public Vector2 SpawnPoint => Iso.W(bx + 2.5f, by + gy - 2.5f);
        public Vector2 Center => Iso.W(bx + gx / 2f, by + gy / 2f);

        void Update()
        {
            if (Started || player == null || player.IsDead) return;
            if (Contains((Vector2)player.transform.position + Vector2.up * 0.25f, 1.2f)) Begin();
        }

        void Begin()
        {
            Started = true;
            if (entryGate != null) entryGate.SetActive(true);
            GameRoot.I.EnterRoom(this);
            StartCoroutine(RunWaves());
        }

        IEnumerator RunWaves()
        {
            yield return new WaitForSeconds(Index == 1 ? 1.2f : 0.5f);
            var rng = new System.Random(Index * 31 + System.Environment.TickCount);
            for (Wave = 1; Wave <= WaveCount; Wave++)
            {
                int n = 2 + Index + (Wave - 1);
                var spots = new List<Vector2>();
                for (int tries = 0; spots.Count < n && tries < 400; tries++)
                {
                    float tx = bx + 1.5f + (float)rng.NextDouble() * (gx - 3), ty = by + 1.5f + (float)rng.NextDouble() * (gy - 3);
                    Vector2 p = Iso.W(tx, ty);
                    if (Vector2.Distance(p, player.transform.position) < 3.5f) continue;
                    if (Physics2D.OverlapCircle(p + Vector2.up * 0.25f, 0.5f) != null) continue;
                    bool close = false;
                    foreach (var s in spots) if (Vector2.Distance(s, p) < 1.2f) close = true;
                    if (!close) spots.Add(p);
                }
                var markers = new List<GameObject>();
                foreach (var s in spots)
                {
                    var m = Effects.SpawnMarker(s, new Color(1f, 0.2f, 0.3f, 0.5f));
                    m.transform.localScale = new Vector3(1f, 0.5f, 1f);
                    markers.Add(m);
                }
                yield return new WaitForSeconds(0.7f);
                foreach (var m in markers) Destroy(m);
                foreach (var s in spots)
                {
                    Enemy e = rng.NextDouble() < 0.38 ? (Enemy)Enemy.Spawn<GunnerEnemy>(s) : Enemy.Spawn<ScavengerEnemy>(s);
                    e.transform.SetParent(transform, true);
                    Alive++;
                }
                while (Alive > 0) yield return null;
                yield return new WaitForSeconds(0.5f);
            }
            Wave = WaveCount;
            Clear();
        }

        public void OnEnemyKilled(Enemy e) => Alive = Mathf.Max(0, Alive - 1);

        void Clear()
        {
            Cleared = true;
            Projectile.ClearAll();
            if (exitGate != null) exitGate.SetActive(false);
            var pool = new List<RewardType> { RewardType.FireRate, RewardType.Bounce, RewardType.MaxHp, RewardType.Damage };
            for (int i = 0; i < 3; i++)
            {
                var r = pool[Random.Range(0, pool.Count)];
                pool.Remove(r);
                Pickup.Spawn(transform, Center + new Vector2((i - 1) * 2f, 0f), r);
            }
            if (IsLast) GameRoot.I.OnRunComplete();
            else GreyboxHUD.Message(exitSide == Side.NE ? "街区清理完毕：拿一个奖励，出口在右上方" : "街区清理完毕：拿一个奖励，出口在左上方", 4f);
        }
    }
}
