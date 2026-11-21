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
        public const int GateWidth = 6;      // 门洞 6 格宽：人物放大后 3 格太窄，新的大门图也是按 6 格做的

        public int Wave { get; private set; }
        public int WaveCount { get; private set; }
        /// <summary>房间里还剩几个敌对单位（倒戈的不算）。</summary>
        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var e in Enemy.All) if (e != null && !e.Allied && e.transform.parent == transform) n++;
                return n;
            }
        }
        public bool Cleared { get; private set; }
        public bool Started { get; private set; }
        public int Index { get; private set; }
        public bool IsLast { get; private set; }
        public RoomKind Kind { get; private set; }
        public string EventName { get; private set; }
        RoomKind[] exits = new RoomKind[0];
        readonly List<float> ladderAt = new List<float>();       // 每把梯子在后墙（y = by）上的起始格

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
                                         Side entrySide, int entryAt, Side exitSide, int exitAt, PlayerController player, System.Random rng,
                                         RoomKind kind = RoomKind.Combat, RoomKind[] exits = null)
        {
            var go = new GameObject($"Block {index}");
            go.transform.SetParent(parent, false);
            var rd = go.AddComponent<RoomDirector>();
            rd.Index = index; rd.IsLast = isLast; rd.player = player;
            rd.bx = bx; rd.by = by; rd.gx = gx; rd.gy = gy;
            rd.entrySide = entrySide; rd.entryAt = entryAt; rd.exitSide = exitSide; rd.exitAt = exitAt;
            rd.Kind = kind; rd.exits = exits ?? new RoomKind[0];
            for (int i = 0; i < rd.exits.Length; i++) rd.ladderAt.Add(bx + gx * (i + 1f) / (rd.exits.Length + 1f) - 0.6f);
            rd.Generate(rng);
            rd.BuildLadders();
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
            // 墙根暗影：房子脚下沿墙线往外 1.8 格逐渐变暗，房子像是立在地上而不是贴在上面
            Color dk = new Color(0f, 0f, 0f, 0.5f), cl = new Color(0f, 0f, 0f, 0f);
            Iso.Gradient(t, "WallAO", new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y0 + 1.8f), new Vector2(x0, y0 + 1.8f) }, new[] { dk, dk, cl, cl }, -29978);
            Iso.Gradient(t, "WallAO", new[] { new Vector2(x0, y0), new Vector2(x0 + 1.8f, y0), new Vector2(x0 + 1.8f, y1), new Vector2(x0, y1) }, new[] { dk, cl, cl, dk }, -29978);
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
            // 出口两边为了不挡视线没放房子的那几格，补一段矮墙把缺口封死（比人矮，不挡门和门后的路）
            if (exitSide == Side.NE)
            {
                Wing(new Vector2(Mathf.Max(x0, x0 + exitAt - ExitClear), y0), new Vector2(x0 + exitAt, y0), rng);
                Wing(new Vector2(x0 + exitAt + GateWidth, y0), new Vector2(Mathf.Min(x1, x0 + exitAt + GateWidth + ExitClearFront), y0), rng);
            }
            if (exitSide == Side.NW)
            {
                Wing(new Vector2(x0, Mathf.Max(y0, y0 + exitAt - ExitClear)), new Vector2(x0, y0 + exitAt), rng);
                Wing(new Vector2(x0, y0 + exitAt + GateWidth), new Vector2(x0, Mathf.Min(y1, y0 + exitAt + GateWidth + ExitClearFront)), rng);
            }

            // 出入口前留空，不放掩体
            if (exitSide == Side.NE) blocked.Add(new Rect(x0 + exitAt - 1, y0, GateWidth + 2, 4));
            if (exitSide == Side.NW) blocked.Add(new Rect(x0, y0 + exitAt - 1, 4, GateWidth + 2));
            if (entrySide == Side.NE) blocked.Add(new Rect(x0 + entryAt - 1, y1 - 5, GateWidth + 2, 5));
            if (entrySide == Side.NW) blocked.Add(new Rect(x1 - 5, y0 + entryAt - 1, 5, GateWidth + 2));
            if (entrySide == Side.None) blocked.Add(new Rect(x0, y1 - 6, 6, 6));           // 出生点
            if (Index == 1) blocked.Add(new Rect(x0 + 4, y1 - 8, 10, 8));                     // 零的车
            foreach (var la in ladderAt) blocked.Add(new Rect(la - 1.5f, y0, 4.2f, 4));        // 梯子前面留空
            if (Kind != RoomKind.Combat) blocked.Add(new Rect(x0 + gx / 2f - 6, y0 + gy / 2f - 5, 12, 10));     // 货架 / 事件摆在正中
            int covers = Kind == RoomKind.Combat ? 4 + rng.Next(4) : 1 + rng.Next(2);                 // 房间变大了，掩体多放一两个
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
            var lampCols = new[] { "ffc25a", "ff5a2e", "ffb070", "e0409a" };      // 路灯只用暖色和洋红，不用青色
            for (int i = 0, tries = 0; i < 3 && tries < 100; tries++)
            {
                int lx = x0 + 2 + rng.Next(gx - 4), ly = y0 + 2 + rng.Next(gy - 4);
                var r = new Rect(lx - 0.5f, ly - 0.5f, 1f, 1f);
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                if (hit) continue;
                bool barrel = i == 0;
                FireLife barrelFire = null;
                if (TooClose(barrel ? "firebarrel" : "lamp", new Vector2(lx, ly))) continue;
                Mark(barrel ? "firebarrel" : "lamp", new Vector2(lx, ly));
                blocked.Add(new Rect(lx - 1, ly - 1, 2, 2));
                var sprBarrel = Props.Find("props", "firebarrel");
                var sprLamp = Props.Find("props", "lamp");
                if (barrel && sprBarrel != null)
                {
                    var bgo = Props.Place(t, sprBarrel, new Vector2(lx + 0.45f, ly + 0.45f), false, true);
                    if (sprBarrel.fire.y >= 0f)
                    {
                        barrelFire = Atmosphere.Fire(bgo.transform, (Vector2)bgo.transform.position + sprBarrel.fire);
                        foreach (var gsr in bgo.GetComponentsInChildren<SpriteRenderer>()) if (gsr.name == "Glow") barrelFire.glow = gsr;
                    }
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
                var lampLight = Atmosphere.Lamp(t, Iso.W(lx + 0.2f, ly + 0.2f) + Vector2.up * (barrel ? 0.9f : 1.8f), lc, barrel ? 5f : 8f, barrel ? 1.3f : 1.1f, flicker: barrel);
                if (barrelFire != null) barrelFire.lamp = lampLight.GetComponent<Flicker>();        // 火烧完，光也跟着暗下去
                i++;
            }

            // 杂物：大多靠着后墙，不挡路也不挡子弹
            var junk = Props.Get("props").FindAll(e => e.name != "firebarrel" && e.name != "lamp");
            for (int i = 0, tries = 0; junk.Count > 0 && i < 8 + rng.Next(5) && tries < 200; tries++)
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
                if (car != null) Props.Place(t, car, new Vector2(x0 + 12.5f, y1 - 1.2f), false, true);
            }
            if (Index == 2)
            {
                var booth = Props.Find("unique", "phonebooth");
                if (booth != null)
                {
                    for (int tries = 0; tries < 60; tries++)
                    {
                        float bx2 = x0 + 2 + (float)rng.NextDouble() * (gx - 4), by2 = y0 + 1.2f;
                        var r = new Rect(bx2 - 2.2f, by2 - 1.2f, 3f, 3f);
                        bool hit = false;
                        foreach (var b in blocked) if (b.Overlaps(r)) { hit = true; break; }
                        if (hit) continue;
                        blocked.Add(r);
                        var go = Props.Place(t, booth, new Vector2(bx2, by2), false, true);
                        Atmosphere.Lamp(go.transform, (Vector2)go.transform.position + Vector2.up * 2.4f, Sprites.Hex("ffe2a8"), 3.6f, 1.2f, flicker: true);      // 电话亭里是暖黄的灯，不再发青光
                        Atmosphere.Lamp(go.transform, (Vector2)go.transform.position + Vector2.up * 0.3f, Sprites.Hex("ffd27a"), 2f, 0.9f);
                        break;
                    }
                }
            }

            if (Kind == RoomKind.Combat) PlaceShockPuddles(rng);
            WaveCount = Mathf.Min(4, 2 + (Index - 1) / 2);
        }

        /// <summary>积水区：每间小怪房 1–2 个漏电水坑（第一间只放 1 个），旁边倒着变电箱。</summary>
        void PlaceShockPuddles(System.Random rng)
        {
            int want = Index == 1 ? 1 : 1 + rng.Next(2);
            for (int i = 0, tries = 0; i < want && tries < 150; tries++)
            {
                float px = bx + 5 + (float)rng.NextDouble() * (gx - 10), py = by + 5 + (float)rng.NextDouble() * (gy - 10);
                var pr = new Rect(px - 2.6f, py - 2.6f, 5.2f, 5.2f);
                var box = new Vector2(Mathf.Round(px - 3.6f), Mathf.Round(py - 1.2f));      // 变电箱在水坑左后方
                var br = new Rect(box.x - 0.5f, box.y - 0.5f, 2f, 2f);
                if (box.x < bx + 1 || box.y < by + 1) continue;
                bool hit = false;
                foreach (var b in blocked) if (b.Overlaps(pr) || b.Overlaps(br)) { hit = true; break; }
                if (hit) continue;
                blocked.Add(pr); blocked.Add(br);
                ShockPuddle.Build(transform, new Vector2(px, py), box, rng);
                i++;
            }
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
            const float Clear = ExitClear, ClearFront = ExitClearFront, LowSpan = 6f;
            float g0 = exitSide == Side.NE ? x0 + exitAt - Clear : float.MaxValue, g1 = exitSide == Side.NE ? x0 + exitAt + GateWidth + ClearFront : float.MaxValue;
            // 梯子那几段：房子之间留出缺口，梯子和它背后的墙就嵌在缺口里，不再盖在房子上
            var ladderGaps = new List<Vector2>();
            foreach (var la in ladderAt) ladderGaps.Add(new Vector2(la - LadderArt.LadderOffset, la - LadderArt.LadderOffset + LadderArt.WallTiles));
            maxNE = Mathf.Max(maxNE, FillWall(set, x0, x1, g0, g1, false, x0, y0, rng, g1 + LowSpan, ladderGaps));
            g0 = exitSide == Side.NW ? y0 + exitAt - Clear : float.MaxValue; g1 = exitSide == Side.NW ? y0 + exitAt + GateWidth + ClearFront : float.MaxValue;
            maxNW = Mathf.Max(maxNW, FillWall(set, y0, nwEnd, g0, g1, true, x0, y0, rng, g1 + LowSpan));
            // footY 是按 0.95 收缩过的进深；再加一点余量让房子完整落在平台上
            BackNE = Mathf.Clamp(Mathf.CeilToInt(maxNE / 0.95f + 0.5f), 2, 12);
            BackNW = Mathf.Clamp(Mathf.CeilToInt(maxNW / 0.95f + 0.5f), 2, 12);
        }

        const float ExitClear = 1f, ExitClearFront = 3f;            // 出口两边不放房子的宽度（格），这里改成矮墙

        /// <summary>
        /// 出口旁边的一段矮墙：混凝土墙脚 + 一截拼起来的锈铁皮，到人胸口高。
        /// 和人的前后关系用 IsoBlock 按墙线判断（人在房间里 → 人在前；出口打开后走进门后的路 → 墙在前）。
        /// </summary>
        void Wing(Vector2 a, Vector2 b, System.Random rng)
        {
            float len = (b - a).magnitude;
            if (len < 0.2f) return;
            bool alongX = Mathf.Abs(a.y - b.y) < 0.001f;
            var go = new GameObject("GateWing");
            go.transform.SetParent(transform, false);
            go.transform.position = Iso.W((a.x + b.x) / 2f, (a.y + b.y) / 2f);
            var t = go.transform;
            const float Px = 1f / 64f;                                            // 高度参数里的 1 像素
            Color conc = Sprites.Hex("2a2f36"), concTop = Sprites.Hex("464e58"), concDark = Sprites.Hex("1b1f25");
            Color[] sheet = { Sprites.Hex("5a3422"), Sprites.Hex("4a3a30"), Sprites.Hex("63402a"), Sprites.Hex("3c3530") };
            Iso.Face(t, "WingBase", a, b, 26 * Px, conc, 0, 0f, glow: true);
            Iso.Face(t, "WingFoot", a, b, 4 * Px, concDark, 1, 0f, glow: true);
            Iso.Face(t, "WingCap", a, b, 3 * Px, concTop, 1, 26 * Px, glow: true);
            Vector2 dir = (b - a) / len;
            for (float s = 1f; s < len - 0.2f; s += 1f)                           // 浇筑缝
                Iso.Face(t, "WingSeam", a + dir * s, a + dir * (s + 0.06f), 26 * Px, concDark, 2, 0f, glow: true);
            // 上面一截铁皮：一块块宽窄不一、高低不齐，偶尔缺一块
            for (float s = 0f; s < len - 0.1f;)
            {
                float w = Mathf.Min(len - s, 0.6f + (float)rng.NextDouble() * 0.7f);
                if (rng.NextDouble() > 0.15)
                {
                    float h = (20 + rng.Next(14)) * Px;
                    var c = sheet[rng.Next(sheet.Length)];
                    Iso.Face(t, "WingSheet", a + dir * s, a + dir * (s + w), h, c, 3, 29 * Px, glow: true);
                    var hi = c * 1.25f; hi.a = 1f;
                    Iso.Face(t, "WingSheetTop", a + dir * s, a + dir * (s + w), 2 * Px, hi, 4, 29 * Px + h - 2 * Px, glow: true);
                }
                s += w;
            }
            for (float s = 0f; s <= len + 0.01f; s += Mathf.Max(0.5f, len / Mathf.Max(1, Mathf.Round(len / 1.5f))))   // 立柱
                Iso.Face(t, "WingPost", a + dir * Mathf.Min(s, len - 0.08f), a + dir * Mathf.Min(s + 0.08f, len), 56 * Px, Sprites.Hex("2e2620"), 5, 0f, glow: true);
            // 碰撞不用另加：后墙除了门洞本来就整条有碰撞
            var sg = go.AddComponent<UnityEngine.Rendering.SortingGroup>();
            float topY = Mathf.Max(Iso.W(a.x, a.y).y, Iso.W(b.x, b.y).y);
            sg.sortingOrder = Buildings.BackLayer + Iso.Order(topY) + 8;          // 平时和房子一层，人走近时由 IsoBlock 判断前后
            var ib = go.AddComponent<IsoBlock>();
            ib.x0 = Mathf.Min(a.x, b.x) - (alongX ? 0f : 0.15f); ib.x1 = Mathf.Max(a.x, b.x);
            ib.y0 = Mathf.Min(a.y, b.y) - (alongX ? 0.15f : 0f); ib.y1 = Mathf.Max(a.y, b.y);
        }

        const float Overlap = 0.9f;
        const float LowHeight = 7f * Buildings.Scale;                 // 原图高度不超过 224 像素的算矮房子                                   // 下一栋往前挪 10%，和上一栋稍微叠一点
        static float Facade(Buildings.Entry e) => e.footX / 0.95f;    // 正面实际宽度（格）

        /// <summary>
        /// 沿一面墙从 start 排到 end（格子坐标：NE 墙是 x，NW 墙是 y），跳过 [g0, g1) 出口段。返回这面墙上房子最深的进深。
        /// </summary>
        /// <summary>后墙（右上那面）上摆好的房子：x = 起点格，y = 终点格，z = 高度（单位）。用来给梯子的墙定高度。</summary>
        readonly List<Vector3> neSpans = new List<Vector3>();

        float FillWall(List<Buildings.Entry> set, float start, float end, float g0, float g1, bool nw, int x0, int y0, System.Random rng, float lowUntil = float.MinValue, List<Vector2> extraGaps = null)
        {
            float c = start, maxDepth = 0f;
            Buildings.Entry last = null;                                   // 紧挨着的两栋不能一样
            var gaps = new List<Vector2>();
            if (g0 < float.MaxValue) gaps.Add(new Vector2(g0, g1));
            if (extraGaps != null) gaps.AddRange(extraGaps);
            while (c < end - 0.5f)
            {
                bool skip = false;
                foreach (var g in gaps) if (c >= g.x && c < g.y) { c = g.y; last = null; skip = true; break; }
                if (skip) continue;
                float limit = end;
                foreach (var g in gaps) if (g.x > c) limit = Mathf.Min(limit, g.x);
                float room = limit - c;
                bool lowOnly = c >= g1 && c < lowUntil;
                var fit = set.FindAll(b => b != last && Facade(b) * Overlap <= room + 0.3f && (!lowOnly || b.height <= LowHeight));
                if (fit.Count > 0)
                {
                    var e = fit[rng.Next(fit.Count)];
                    float f = Facade(e);
                    float lim = limit < end ? limit : limit + 0.3f;                  // 挨着出口矮墙的那一栋不能伸进去
                    Vector2 front = nw ? new Vector2(x0, Mathf.Min(c + f, lim)) : new Vector2(Mathf.Min(c + f, lim), y0);
                    Buildings.Place(transform, e, front, nw, rng);
                    if (!nw) neSpans.Add(new Vector3(c, Mathf.Min(c + f, lim), e.height));
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
            Destroy(go.GetComponent<IsoBlock>());                     // 夹在房子之间的杂物永远排在房子那一层
            go.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = Buildings.BackLayer + Iso.Order(go.transform.position.y) + 6;
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
            var spr = Props.Find("gates", front ? "gate_fence" : (Index % 2 == 1 ? "gate_shutter" : "gate_flood"));     // 出口：卷帘闸和防洪闸板轮着用
            if (spr != null)
            {
                bool alongX = Mathf.Abs(a.y - b.y) < 0.001f;
                Vector2 low = alongX ? new Vector2(Mathf.Max(a.x, b.x), a.y) : new Vector2(a.x, Mathf.Max(a.y, b.y));
                var placed = Props.Place(go.transform, spr, low, alongX, false, shadow: false);
                // 门是一片薄板，按图的轮廓算出来的占地不准：直接用门所在的那条线（往里 0.15 格的厚度）
                var ib = placed.GetComponent<IsoBlock>();
                if (ib == null) ib = placed.AddComponent<IsoBlock>();
                ib.x0 = Mathf.Min(a.x, b.x) - (alongX ? 0f : 0.15f); ib.x1 = Mathf.Max(a.x, b.x);
                ib.y0 = Mathf.Min(a.y, b.y) - (alongX ? 0.15f : 0f); ib.y1 = Mathf.Max(a.y, b.y);
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
            if (Kind != RoomKind.Combat)
            {
                Cleared = true;
                if (Kind == RoomKind.Shop) SetupShop();
                else if (Kind == RoomKind.Signal) Invasion(new Vector2(0f, -1.4f), 3);
                else SetupEvent();
                GameRoot.I.EnterRoom(this);
                if (IsLast) SpawnProtocols();
                SpawnLadders();
                return;
            }
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
                // 编队：近战为主，远程（枪手、炮台）最多三分之一，而且只跟着近战一起出现；
                // 离玩家近的位置刷近战，远的位置刷远程，让近战顶在前面
                spots.Sort((a, b) => Vector2.Distance(a, player.transform.position).CompareTo(Vector2.Distance(b, player.transform.position)));
                int ranged = Index == 1 && Wave == 1 ? 0 : Mathf.Min(spots.Count / 3, 1 + (Index - 1) / 2);
                if (spots.Count - ranged < 1) ranged = Mathf.Max(0, spots.Count - 1);
                bool turret = Wave == 1 && Index >= 2;                       // 第 2 间开始，第一波的远程里有一座炮台（能被骇入策反）
                if (turret && ranged == 0 && spots.Count > 1) ranged = 1;
                int brutes = 0;
                for (int k = 0; k < spots.Count; k++)
                {
                    var s = spots[k];
                    Enemy e;
                    if (k >= spots.Count - ranged)
                    {
                        e = turret ? Enemy.Spawn<TurretEnemy>(s) : Enemy.Spawn<GunnerEnemy>(s);
                        turret = false;
                    }
                    else
                    {
                        double r = rng.NextDouble();
                        if (Index >= 2 && r < 0.2 && brutes < 1 + Index / 3) { e = Enemy.Spawn<BruteEnemy>(s); brutes++; }
                        else if (Index >= 2 && r < 0.5) e = Enemy.Spawn<StabberEnemy>(s);
                        else e = Enemy.Spawn<ScavengerEnemy>(s);
                    }
                    e.transform.SetParent(transform, true);
                }
                if (Wave == 1) Protocols.OnFirstWave(this);
                while (Alive > 0) yield return null;
                yield return new WaitForSeconds(0.5f);
            }
            Wave = WaveCount;
            Clear();
        }

        public void OnEnemyKilled(Enemy e) { }

        void Clear()
        {
            Cleared = true;
            Projectile.ClearAll(keepThrown: true);              // 还在飞的投掷物落在原地，可以捡回来
            player.OnRoomCleared();
            foreach (var e in Enemy.All.ToArray()) if (e != null && e.Allied && e.transform.parent == transform) e.Dismiss();   // 倒戈的单位断电下线
            if (exitGate != null) exitGate.SetActive(false);
            Chest.Spawn(transform, Center + (IsLast ? new Vector2(0f, -1.6f) : Vector2.zero));
            Sfx.Play("room_clear");
            if (IsLast) SpawnProtocols();
            SpawnLadders();
            if (IsLast) { }                                         // 入侵台词由 Invasion 显示
            else if (exits.Length > 1) GreyboxHUD.Message("Cleared. Open the crate. Four ladders: combat, black market, event, ghost channel — pick one", 4.5f);
            else GreyboxHUD.Message("Cleared. Open the crate, then climb the ladder on the back wall", 3.5f);
        }

        /// <summary>第一部分结束：幽灵频道入侵，一个 AI 拿出它的三个协议。</summary>
        void SpawnProtocols() => Invasion(new Vector2(0f, 1.2f), 1);

        /// <summary>幽灵频道入侵：画面抖一下、冒故障块，AI 说一句话，摆出三个协议（拿一个，其余消失）。</summary>
        void Invasion(Vector2 offset, int group)
        {
            var list = Protocols.Offer(new System.Random(), player, out int ai);
            if (list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                Vector2 at = Center + offset + new Vector2((i - 1) * 3.4f, 0f);
                Pickup.Spawn(transform, at, list[i], group);
                PixelFx.Play(PixelFx.Glitch(14), at, 0f, Protocols.AiColor[list[i].ai], 14f);
            }
            CameraRig.Shake(0.12f);
            Sfx.Play("protocol_appear");
            Effects.Flash(Center + offset, Protocols.AiColor[ai], 6f, 1.5f, 0.25f);
            GreyboxHUD.Message($"GHOST CHANNEL BREACH — {Protocols.AiLine[ai]}\nChoose an AI protocol", 4f);
        }

        /// <summary>缺口里那面墙的高度：取左右两栋邻居房子的平均高度再矮一点，夹在 7.5–12 之间。</summary>
        float GapWallHeight(float gapStart)
        {
            float gapEnd = gapStart + LadderArt.WallTiles, sum = 0f; int n = 0;
            foreach (var sp in neSpans)
            {
                bool left = sp.y > gapStart - 2f && sp.y <= gapStart + 0.5f;
                bool right = sp.x >= gapEnd - 0.5f && sp.x < gapEnd + 2f;
                if (left || right) { sum += sp.z; n++; }
            }
            return n == 0 ? 9.5f : Mathf.Clamp(sum / n * 0.88f, 7.5f, 12f);
        }

        readonly List<Ladder> ladders = new List<Ladder>();

        /// <summary>房间一生成就把梯子墙立好（梯子先收着），清完房间再放下梯子。</summary>
        void BuildLadders()
        {
            for (int i = 0; i < exits.Length; i++)
            {
                var k = exits[i];
                string title = k switch
                {
                    RoomKind.Boss => "Boss",
                    RoomKind.Shop => "Black Market",
                    RoomKind.Event => "Event",
                    RoomKind.Signal => "Ghost Channel",
                    _ => exits.Length > 1 ? "Combat" : "Next Room",
                };
                ladders.Add(Ladder.Build(transform, ladderAt[i], by, k, title, fork: exits.Length > 1 && k == RoomKind.Combat,
                                         height: GapWallHeight(ladderAt[i] - LadderArt.LadderOffset), locked: true));
            }
        }

        void SpawnLadders()
        {
            for (int i = 0; i < ladders.Count; i++)
            {
                if (ladders[i] == null || !ladders[i].Locked) continue;
                ladders[i].Unlock();
                Effects.Sparks(Iso.W(ladderAt[i] + 0.6f, by + 0.5f), Sprites.Hex("ffb070"), 8, 2f);
            }
        }

        // ---------------------------------------------------------------- 黑市
        /// <summary>老鬼的黑市：3 把武器 + 4 种补给，可以花 15 信用点刷新一次武器货架。</summary>
        void SetupShop()
        {
            var t = transform;
            var rng = new System.Random();
            var shelf = new List<WeaponPickup>();
            void Stock()
            {
                foreach (var w in shelf) if (w != null) Destroy(w.gameObject);
                shelf.Clear();
                var taken = new List<WeaponDef> { player.Slots[0], player.Slots[1] };
                for (int i = 0; i < 3; i++)
                {
                    var w = Weapons.Roll(rng, Weapons.ShopOdds, taken.ToArray());
                    taken.Add(w);
                    shelf.Add(WeaponPickup.Spawn(t, Center + new Vector2((i - 1) * 2.6f, 1.4f), w, w.price));
                }
            }
            Stock();
            var items = new[] { Item.RedLine, Item.Gel, Item.Cell, Item.Overload, Item.Ammo };
            for (int i = 0; i < items.Length; i++)
                ItemPickup.Spawn(t, Center + new Vector2((i - 2f) * 2.2f, -1.0f), items[i], Items.Price(items[i]));
            // 两件义体（数值增益），₡90 一件
            var chips = Buffs.Roll(rng, 2, player, true, BuffGroup.Attack, BuffGroup.Defense, BuffGroup.Weapon, BuffGroup.Resource);
            for (int i = 0; i < chips.Count; i++)
            {
                var b = chips[i];
                Vector2 at = Center + new Vector2(i == 0 ? -5.2f : 5.2f, 1.4f);
                var chip = Loot.Holder(t, "义体", at);
                Loot.Px(chip.transform, 10, 10, Sprites.Hex("1e1c22"), new Vector2(0, 5));
                Loot.Px(chip.transform, 6, 6, Buffs.GroupColor[(int)b.group], new Vector2(0, 5), 1);
                ActionPoint.Spawn(t, at, $"{b.name}  ₡90", $"E  Install implant {b.name} (₡90)\n{b.desc}", p =>
                {
                    if (p.credits < 90) { GreyboxHUD.Message($"Not enough credits (need ₡{90 - p.credits} more)", 1.5f); return false; }
                    p.credits -= 90;
                    p.ApplyBuff(b);
                    Effects.Sparks(at, Buffs.GroupColor[(int)b.group], 8, 2.5f);
                    GreyboxHUD.Message($"Implant installed: {b.name}  {b.desc}", 2f);
                    Destroy(chip);
                    return true;
                });
            }
            var stall = Loot.Holder(t, "Stall", Center + new Vector2(0f, 3.0f));
            Loot.Px(stall.transform, 40, 18, Sprites.Hex("2a2420"), new Vector2(0, 9));
            Loot.Px(stall.transform, 40, 3, Sprites.Hex("e0409a"), new Vector2(0, 19), 1);
            Atmosphere.Lamp(t, Center + new Vector2(0f, 3.8f), Sprites.Hex("e0409a"), 6f, 1.0f);
            ActionPoint.Spawn(t, Center + new Vector2(0f, 3.0f), "Old Ghost's Market", "E  Restock the weapon rack (₡25, once)", p =>
            {
                if (p.credits < 25) { GreyboxHUD.Message($"Not enough credits (need ₡{25 - p.credits} more)", 1.5f); return false; }
                p.credits -= 25;
                Stock();
                GreyboxHUD.Message("OLD GHOST: \"Fresh stock. Don't ask where from.\"", 2f);
                return true;
            }).radius = 1.6f;
        }

        // ---------------------------------------------------------------- 随机事件（占位：三选一个出现）
        void SetupEvent()
        {
            var t = transform;
            var pos = Center + new Vector2(0f, 0.6f);
            var post = Loot.Holder(t, "Event", pos);
            int pick = Random.Range(0, 4);
            if (pick == 3)
            {
                EventName = "Ripperdoc";
                Loot.Px(post.transform, 22, 30, Sprites.Hex("2a2830"), new Vector2(0, 15));
                Loot.Px(post.transform, 18, 4, Sprites.Hex("e0409a"), new Vector2(0, 26), 1);
                Loot.Px(post.transform, 6, 6, Loot.HealCol, new Vector2(0, 16), 1);
                Atmosphere.Lamp(t, pos + Vector2.up * 1f, Sprites.Hex("e0409a"), 4.5f, 1f);
                ActionPoint.Spawn(t, pos, "Ripperdoc", "E  Lie on the table: one free implant (choose 1 of 3)", p =>
                {
                    var list = Buffs.Roll(new System.Random(), 3, p, true, BuffGroup.Defense, BuffGroup.Weapon);
                    for (int i = 0; i < list.Count; i++)
                        Pickup.Spawn(t, pos + new Vector2((i - 1) * 3.2f, -1.6f), list[i], 2);
                    GreyboxHUD.Message("RIPPERDOC: \"Pick one. Anesthetic is extra. Kidding.\"", 2.5f);
                    return true;
                });
            }
            else if (pick == 0)
            {
                EventName = "Back-Alley Doc";
                Loot.Px(post.transform, 14, 30, Sprites.Hex("2c2a2e"), new Vector2(0, 15));
                Loot.Px(post.transform, 10, 6, Loot.HealCol, new Vector2(0, 24), 1);
                Atmosphere.Lamp(t, pos + Vector2.up * 1f, Loot.HealCol, 4f, 0.9f);
                ActionPoint.Spawn(t, pos, "Back-Alley Doc", "E  Sell blood to the back-alley doc: HP -1, Power +50", p =>
                {
                    if (p.Hp.hp <= 1f) { GreyboxHUD.Message("DOC: \"Any more and you'll be empty.\"", 2f); return false; }
                    p.Hp.hp -= 1f; p.AddEnergy(50f);
                    Effects.Sparks(p.transform.position + Vector3.up, Loot.HealCol, 8, 2f);
                    GreyboxHUD.Message("HP -1, Power +50", 2f);
                    return true;
                });
            }
            else if (pick == 1)
            {
                EventName = "Slot Machine";
                Loot.Px(post.transform, 18, 28, Sprites.Hex("3a2a1e"), new Vector2(0, 14));
                Loot.Px(post.transform, 14, 8, Loot.CreditCol, new Vector2(0, 20), 1);
                Atmosphere.Lamp(t, pos + Vector2.up * 1f, Loot.CreditCol, 4f, 0.9f);
                ActionPoint.Spawn(t, pos, "Slot Machine", "E  Insert ₡70: spits out a random weapon (Uncommon+)", p =>
                {
                    if (p.credits < 70) { GreyboxHUD.Message($"Not enough credits (need ₡{70 - p.credits} more)", 1.5f); return false; }
                    p.credits -= 70;
                    WeaponPickup.Spawn(t, pos + new Vector2(0f, -1.2f), Weapons.Roll(new System.Random(), Weapons.EventOdds, p.Slots[0], p.Slots[1]));
                    GreyboxHUD.Message("CLUNK — a weapon drops out", 2f);
                    return true;
                });
            }
            else
            {
                EventName = "Drifted Body";
                Loot.Px(post.transform, 30, 8, Sprites.Hex("3a3438"), new Vector2(0, 4));
                Loot.Px(post.transform, 8, 6, Sprites.Hex("5a4a44"), new Vector2(-13, 6), 1);
                ActionPoint.Spawn(t, pos, "Drifted Body", "E  Search the body: +35 credits, 30% chance his implant alarm goes off (HP -1)", p =>
                {
                    p.credits += 35;
                    if (Random.value < 0.3f && p.Hp.hp > 1f)
                    {
                        p.Hp.hp -= 1f;
                        CameraRig.Shake(0.15f);
                        Effects.Flash(p.transform.position + Vector3.up, Loot.HealCol, 2f, 1.2f, 0.25f);
                        GreyboxHUD.Message("Implant alarm blew up! +35 credits, HP -1", 2f);
                    }
                    else GreyboxHUD.Message("+35 credits", 1.6f);
                    return true;
                });
            }
        }

        /// <summary>镜头能去的范围：只框住这一间（房子要往上留出高度）。</summary>
        public Rect ViewRect
        {
            get
            {
                Vector2 a = Iso.W(bx - BackNW, by - BackNE), b = Iso.W(bx + gx, by - BackNE), c = Iso.W(bx + gx, by + gy), d = Iso.W(bx - BackNW, by + gy);
                float xMin = Mathf.Min(a.x, b.x, c.x, d.x) - 1f, xMax = Mathf.Max(a.x, b.x, c.x, d.x) + 1f;
                float yMin = Mathf.Min(a.y, b.y, c.y, d.y) - 2f, yMax = Mathf.Max(a.y, b.y, c.y, d.y) + 6f;
                return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }
    }
}
