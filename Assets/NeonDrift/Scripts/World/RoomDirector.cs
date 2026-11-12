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
        // 已经摆下的物件（名字、格子位置），用来避免同一种东西挨在一起
        readonly List<KeyValuePair<string, Vector2>> placed = new List<KeyValuePair<string, Vector2>>();
        const float SameGap = 6f;

        bool TooClose(string name, Vector2 tile)
        {
            foreach (var p in placed) if (p.Key == name && (p.Value - tile).sqrMagnitude < SameGap * SameGap) return true;
            return false;
        }

        void Mark(string name, Vector2 tile) => placed.Add(new KeyValuePair<string, Vector2>(name, tile));
        GameObject exitGate, entryGate;
        readonly List<Rect> blocked = new List<Rect>();

        static readonly Color FloorCol = Sprites.Hex("2a3444");
        static readonly Color StripeCol = new Color(1f, 1f, 1f, 0.025f);
        static readonly Color WallCol = Sprites.Hex("2c3442");
        static readonly Color WallTop = Sprites.Hex("1a2433");
        static readonly Color RailCol = Sprites.Hex("b5622e");
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

            // 先摆房子：要知道房子有多深，平台才往后铺多少
            PlaceBuildings(x0, y0, x1, y1, rng);
            int dNE = BackNE, dNW = BackNW;
            // 平台：可走区域 + 房子底下那一圈，是同一块地面（贴图按格子坐标铺，几块之间没有接缝）
            Iso.Ground(t, "Floor", x0, y0, x1, y1, FloorCol, -30000);
            Iso.Ground(t, "FloorNE", x0, y0 - dNE, x1, y0, FloorCol, -30000);
            Iso.Ground(t, "FloorNW", x0 - dNW, y0, x0, y1, FloorCol, -30000);
            Iso.Ground(t, "FloorCorner", x0 - dNW, y0 - dNE, x0, y0, FloorCol, -30000);
            // 往后扩出来那段平台朝镜头的两条边：厚板、水管、桩子、护栏
            var prng = new System.Random(x0 * 7 + y0 * 13);
            Platform.Edge(t, new Vector2(x0 - dNW, y1), new Vector2(x0, y1), prng);
            Platform.Fence(t, new Vector2(x0 - dNW, y1), new Vector2(x0, y1), false, prng);
            Platform.Edge(t, new Vector2(x1, y0 - dNE), new Vector2(x1, y0), prng);
            Platform.Fence(t, new Vector2(x1, y0 - dNE), new Vector2(x1, y0), false, prng);
            // 平台最后面的两条边也围上护栏，出口那条街经过的地方留空
            float ng0 = exitSide == Side.NE ? exitAt + dNW : -1f, nw0 = exitSide == Side.NW ? exitAt + dNE : -1f;
            Platform.Fence(t, new Vector2(x0 - dNW, y0 - dNE), new Vector2(x1, y0 - dNE), true, prng, ng0, ng0 + GateWidth);
            Platform.Fence(t, new Vector2(x0 - dNW, y0 - dNE), new Vector2(x0 - dNW, y1), true, prng, nw0, nw0 + GateWidth);
            // 地面贴花：水坑、井盖、格栅、烟头
            var decals = Props.Get("decals");
            for (int i = 0; decals.Count > 0 && i < 6 + rng.Next(4); i++)
            {
                var e = decals[rng.Next(decals.Count)];
                var dp = new Vector2(x0 + 2 + (float)rng.NextDouble() * (gx - 4), y0 + 2 + (float)rng.NextDouble() * (gy - 4));
                if (TooClose(e.name, dp)) continue;
                Mark(e.name, dp);
                Props.Decal(t, e, dp, rng.Next(2) == 0, -29985);
            }
            for (int i = 0; decals.Count == 0 && i < 3; i++)
            {
                var c = Iso.W(x0 + 3 + (float)rng.NextDouble() * (gx - 6), y0 + 3 + (float)rng.NextDouble() * (gy - 6));
                var pud = Sprites.Make(t, "Puddle", Sprites.Circle(64), WaterCol, -29970, c);
                float s = 0.8f + (float)rng.NextDouble() * 0.8f;
                pud.transform.localScale = new Vector3(s * 1.41f * Iso.S, s * 0.7f * Iso.S, 1f);
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
            if (Index == 1) blocked.Add(new Rect(x0 + 4, y1 - 4, 4, 4));                     // 零的车
            int covers = 3 + rng.Next(3);
            for (int i = 0, tries = 0; i < covers && tries < 200; tries++)
            {
                var coverSet = Props.Get("covers");
                var ce = coverSet.Count > 0 ? coverSet[rng.Next(coverSet.Count)] : null;
                bool fl = rng.Next(2) == 0;
                float cfx = ce == null ? 1 : Mathf.Max(1f, fl ? ce.footY : ce.footX), cfy = ce == null ? 1 : Mathf.Max(1f, fl ? ce.footX : ce.footY);
                int w = ce == null ? 1 + rng.Next(2) : Mathf.CeilToInt(cfx), h = ce == null ? 1 + rng.Next(2) : Mathf.CeilToInt(cfy);
                int cx = x0 + 2 + rng.Next(Mathf.Max(1, gx - 4 - w)), cy = y0 + 2 + rng.Next(Mathf.Max(1, gy - 4 - h));
                var r = new Rect(cx - 1, cy - 1, w + 2, h + 2);
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                if (hit || (ce != null && TooClose(ce.name, new Vector2(cx, cy)))) continue;
                blocked.Add(r);
                if (ce != null) Mark(ce.name, new Vector2(cx, cy));
                if (ce != null) Props.Place(t, ce, new Vector2(cx + cfx, cy + cfy), fl, true);
                else Iso.Box(t, "Cover", cx, cy, cx + w, cy + h, 0.5f + (float)rng.NextDouble() * 0.5f, Sprites.Hex(rng.Next(2) == 0 ? "3a3e42" : "4a3a2a"));
                i++;
            }

            // 路灯和火桶：街区里主要的光源
            var lampCols = new[] { "ffc25a", "ff5a2e", "35e0d0", "e0409a" };
            for (int i = 0, tries = 0; i < 3 && tries < 100; tries++)
            {
                int lx = x0 + 2 + rng.Next(gx - 4), ly = y0 + 2 + rng.Next(gy - 4);
                var r = new Rect(lx - 0.5f, ly - 0.5f, 1f, 1f);
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                if (hit) continue;
                bool barrel = i == 0;
                if (TooClose(barrel ? "firebarrel" : "lamp", new Vector2(lx, ly))) continue;
                Mark(barrel ? "firebarrel" : "lamp", new Vector2(lx, ly));
                blocked.Add(new Rect(lx - 1, ly - 1, 2, 2));
                var sprBarrel = Props.Find("props", "firebarrel");
                var sprLamp = Props.Find("props", "lamp");
                if (barrel && sprBarrel != null)
                {
                    var bgo = Props.Place(t, sprBarrel, new Vector2(lx + 0.45f, ly + 0.45f), false, true);
                    if (sprBarrel.fire.y >= 0f) Atmosphere.Fire(bgo.transform, (Vector2)bgo.transform.position + sprBarrel.fire);
                }
                else if (!barrel && sprLamp != null) Props.Place(t, sprLamp, new Vector2(lx + 0.2f, ly + 0.2f), rng.Next(2) == 0, true);
                else if (barrel)
                {
                    Iso.Box(t, "FireBarrel", lx, ly, lx + 0.45f, ly + 0.45f, 0.45f, Sprites.Hex("5a3a26"));
                    var top = Iso.W(lx + 0.22f, ly + 0.22f) + Vector2.up * 0.45f * Iso.S;
                    Iso.Poly(t, "Fire", new[] { top + new Vector2(-0.22f, 0f), top + new Vector2(0.22f, 0f), top + new Vector2(0.05f, 0.5f), top + new Vector2(-0.08f, 0.35f) },
                             Sprites.Hex("ff8a2e"), Iso.Order(Iso.W(lx + 0.45f, ly + 0.45f).y) + 1, glow: true);
                }
                else
                {
                    Iso.Box(t, "LampPost", lx, ly, lx + 0.12f, ly + 0.12f, 2.2f, Sprites.Hex("3a4044"));
                    var head = Iso.W(lx + 0.06f, ly + 0.06f) + Vector2.up * 2.2f * Iso.S;
                    Iso.Poly(t, "LampHead", new[] { head + new Vector2(-0.18f, -0.08f), head + new Vector2(0.18f, -0.08f), head + new Vector2(0.18f, 0.06f), head + new Vector2(-0.18f, 0.06f) },
                             Sprites.Hex("fff0c8"), Iso.Order(Iso.W(lx + 0.12f, ly + 0.12f).y) + 1, glow: true);
                }
                var lc = Sprites.Hex(barrel ? "ff7a2e" : lampCols[rng.Next(lampCols.Length)]);
                Atmosphere.Lamp(t, Iso.W(lx + 0.2f, ly + 0.2f) + Vector2.up * (barrel ? 0.9f : 1.2f), lc, barrel ? 5f : 7f, barrel ? 1.3f : 1.1f, flicker: barrel);
                i++;
            }

            // 杂物：大多靠着后墙，不挡路也不挡子弹
            var junk = Props.Get("props").FindAll(e => e.name != "firebarrel" && e.name != "lamp");
            for (int i = 0, tries = 0; junk.Count > 0 && i < 6 + rng.Next(4) && tries < 200; tries++)
            {
                bool nearNE = rng.Next(2) == 0;
                float px = nearNE ? x0 + 1.5f + (float)rng.NextDouble() * (gx - 3) : x0 + 0.6f + (float)rng.NextDouble() * 1.4f;
                float py = nearNE ? y0 + 0.6f + (float)rng.NextDouble() * 1.4f : y0 + 1.5f + (float)rng.NextDouble() * (gy - 3);
                var r = new Rect(px - 1f, py - 1f, 1f, 1f);
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                var je = junk[rng.Next(junk.Count)];
                if (hit || TooClose(je.name, new Vector2(px, py))) continue;
                Mark(je.name, new Vector2(px, py));
                Props.Place(t, je, new Vector2(px, py), rng.Next(2) == 0, false);
                i++;
            }

            // 只出现一次的：零的车停在诊所门口；电话亭藏在第二个街区
            if (Index == 1)
            {
                var car = Props.Find("unique", "zero_car");
                if (car != null) Props.Place(t, car, new Vector2(x0 + 6.8f, y1 - 1.2f), false, true);
            }
            if (Index == 2)
            {
                var booth = Props.Find("unique", "phonebooth");
                if (booth != null)
                {
                    for (int tries = 0; tries < 60; tries++)
                    {
                        float bx2 = x0 + 2 + (float)rng.NextDouble() * (gx - 4), by2 = y0 + 1.2f;
                        var r = new Rect(bx2 - 1.5f, by2 - 1.2f, 2f, 2f);
                        bool hit = false;
                        foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                        if (hit) continue;
                        blocked.Add(r);
                        var go = Props.Place(t, booth, new Vector2(bx2, by2), false, true);
                        Atmosphere.Lamp(go.transform, (Vector2)go.transform.position + Vector2.up * 1.6f, Sprites.Hex("9af6ff"), 3.5f, 1.4f, flicker: true);
                        Atmosphere.Lamp(go.transform, (Vector2)go.transform.position + Vector2.up * 0.3f, Sprites.Hex("ffd27a"), 2f, 0.9f);
                        break;
                    }
                }
            }

            WaveCount = Mathf.Min(4, 2 + (Index - 1) / 2);
        }

        /// <summary>房子后面那一圈平台的深度（格），按实际摆上去的房子最深的那栋算。</summary>
        public int BackNE { get; private set; } = 2;
        public int BackNW { get; private set; } = 2;

        /// <summary>
        /// 沿后方两面墙摆房子：按每栋房子实际的正面宽度一栋紧挨一栋排（相邻的稍微重叠一点，不留缝），
        /// 剩下塞不进房子的零碎空间用纸箱、垃圾袋、油桶这类杂物补上；出口两边各空 1 格。
        /// </summary>
        void PlaceBuildings(int x0, int y0, int x1, int y1, System.Random rng)
        {
            var set = Buildings.All.FindAll(b => string.IsNullOrEmpty(b.unique));
            var clinic = Buildings.All.Find(b => b.unique == "start");
            if (set.Count == 0) return;
            float maxNE = 0f, maxNW = 0f;
            float nwEnd = y1;
            if (Index == 1 && clinic != null && exitSide != Side.NW)          // 回声诊所：第一个街区，左上墙最靠近出生点的那一段
            {
                Buildings.Place(transform, clinic, new Vector2(x0, y1), true, rng);
                nwEnd = y1 - Facade(clinic) * Overlap;
                maxNW = clinic.footY;
            }
            // 出口后面的路是往画面上方斜着走的，挡住它的是缺口“靠前”那一侧（墙坐标更大的一侧）的房子：
            // 那一侧多空 2 格，接下来 6 格只放矮房子（车库、窝棚）
            const float Clear = 1f, ClearFront = 3f, LowSpan = 6f;
            float g0 = exitSide == Side.NE ? x0 + exitAt - Clear : float.MaxValue, g1 = exitSide == Side.NE ? x0 + exitAt + GateWidth + ClearFront : float.MaxValue;
            maxNE = Mathf.Max(maxNE, FillWall(set, x0, x1, g0, g1, false, x0, y0, rng, g1 + LowSpan));
            g0 = exitSide == Side.NW ? y0 + exitAt - Clear : float.MaxValue; g1 = exitSide == Side.NW ? y0 + exitAt + GateWidth + ClearFront : float.MaxValue;
            maxNW = Mathf.Max(maxNW, FillWall(set, y0, nwEnd, g0, g1, true, x0, y0, rng, g1 + LowSpan));
            // footY 是按 0.95 收缩过的进深；再加一点余量让房子完整落在平台上
            BackNE = Mathf.Clamp(Mathf.CeilToInt(maxNE / 0.95f + 0.5f), 2, 8);
            BackNW = Mathf.Clamp(Mathf.CeilToInt(maxNW / 0.95f + 0.5f), 2, 8);
        }

        const float Overlap = 0.9f;
        const float LowHeight = 7f;                                   // 高度不超过 7 个单位（224 像素）算矮房子                                   // 下一栋往前挪 10%，和上一栋稍微叠一点
        static float Facade(Buildings.Entry e) => e.footX / 0.95f;    // 正面实际宽度（格）

        /// <summary>
        /// 沿一面墙从 start 排到 end（格子坐标：NE 墙是 x，NW 墙是 y），跳过 [g0, g1) 出口段。返回这面墙上房子最深的进深。
        /// </summary>
        float FillWall(List<Buildings.Entry> set, float start, float end, float g0, float g1, bool nw, int x0, int y0, System.Random rng, float lowUntil = float.MinValue)
        {
            float c = start, maxDepth = 0f;
            Buildings.Entry last = null;                                   // 紧挨着的两栋不能一样
            while (c < end - 0.5f)
            {
                if (c >= g0 && c < g1) { c = g1; last = null; continue; }
                float limit = c < g0 ? Mathf.Min(g0, end) : end;
                float room = limit - c;
                bool lowOnly = c >= g1 && c < lowUntil;
                var fit = set.FindAll(b => b != last && Facade(b) * Overlap <= room + 0.3f && (!lowOnly || b.height <= LowHeight));
                if (fit.Count > 0)
                {
                    var e = fit[rng.Next(fit.Count)];
                    float f = Facade(e);
                    Vector2 front = nw ? new Vector2(x0, Mathf.Min(c + f, limit + 0.3f)) : new Vector2(Mathf.Min(c + f, limit + 0.3f), y0);
                    Buildings.Place(transform, e, front, nw, rng);
                    maxDepth = Mathf.Max(maxDepth, e.footY);
                    last = e;
                    c += f * Overlap;
                }
                else
                {
                    c += Filler(c, limit, nw, x0, y0, rng);                // 房子塞不下了，用杂物补
                    last = null;
                }
            }
            return maxDepth;
        }

        /// <summary>在两栋房子之间的零碎空地上摆一件杂物，排在房子那一层。返回占了多宽。</summary>
        float Filler(float c, float limit, bool nw, int x0, int y0, System.Random rng)
        {
            float room = limit - c;
            var names = new[] { "boxes", "bags", "crates", "drums", "dumpster", "tires" };
            var pool = new List<Props.Entry>();
            foreach (var n in names)
            {
                var e = Props.Find("props", n) ?? Props.Find("covers", n);
                if (e != null && e.footX / 0.85f <= room + 0.2f) pool.Add(e);      // 沿墙的宽度：翻不翻转都是 footX
            }
            if (pool.Count == 0) return Mathf.Max(0.5f, room);           // 连杂物都放不下：这点缝就留着
            var pick = pool[rng.Next(pool.Count)];
            float w = pick.footX / 0.85f;
            Vector2 front = nw ? new Vector2(x0 - 0.3f, c + w) : new Vector2(c + w, y0 - 0.3f);
            var go = Props.Place(transform, pick, front, nw, false);
            Destroy(go.GetComponent<YSort>());
            go.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = Buildings.BackLayer + Iso.Order(go.transform.position.y) + 4;
            return Mathf.Max(0.6f, w * 0.95f);
        }

        void WallSeg(Vector2 a, Vector2 b, bool lit)
        {
            float topY = Mathf.Max(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y);
            int order = Buildings.BackLayer - 3000 + Iso.Order(topY + 0.05f);
            // 后墙什么都不画：房子直接立在同一块路面上（以前这里有一道路沿，会在房子脚下露出一条灰线）
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
            Platform.Edge(transform, a, b, new System.Random(Mathf.RoundToInt(a.x * 73 + a.y * 151 + b.x * 7)));
            Platform.Fence(transform, a, b, false, new System.Random(Mathf.RoundToInt(a.x * 31 + a.y * 17 + b.y)));
        }

        GameObject Gate(Vector2 a, Vector2 b, bool front)
        {
            var go = new GameObject(front ? "EntryGate" : "ExitGate");
            go.transform.SetParent(transform, false);
            Iso.Edge(go, a, b);
            float y = front ? Mathf.Min(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y) - 0.05f : Mathf.Max(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y) + 0.05f;
            // 出口用卷帘门，入口用电网；图里的门是沿 x = 常数 的边画的，沿 y = 常数 的边要翻转
            var spr = Props.Find("gates", front ? "gate_fence" : "gate_shutter");
            if (spr != null)
            {
                bool alongX = Mathf.Abs(a.y - b.y) < 0.001f;
                Vector2 low = alongX ? new Vector2(Mathf.Max(a.x, b.x), a.y) : new Vector2(a.x, Mathf.Max(a.y, b.y));
                Props.Place(go.transform, spr, low, alongX, false);
            }
            else
            {
                var c = Sprites.Hex("e43b44"); c.a = 0.7f;
                Iso.Face(go.transform, "Shutter", a, b, front ? 0.9f : WallH, c, Iso.Order(y), glow: true);
            }
            Atmosphere.Lamp(go.transform, Iso.W((a.x + b.x) / 2f, (a.y + b.y) / 2f) + Vector2.up * 0.8f, Sprites.Hex("ff3a3a"), 4f, 1.1f);
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
