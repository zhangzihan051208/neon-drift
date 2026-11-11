using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 每局随机挑选街区并首尾相接：出口只开在后方两面墙上（右上或左上），
    /// 所以整条路线一直往画面上方走，街区之间用一小段街道无缝连接。
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        public readonly List<RoomDirector> Blocks = new List<RoomDirector>();

        static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(16, 14), new Vector2Int(18, 12), new Vector2Int(14, 14), new Vector2Int(20, 11), new Vector2Int(13, 17),
        };

        static readonly Color StreetCol = Sprites.Hex("181b1f");
        static readonly Color RailCol = Sprites.Hex("4a5458");
        static readonly Color WallCol = Sprites.Hex("2a2e32");

        public static WorldBuilder Build(PlayerController player, int blockCount = 5)
        {
            var go = new GameObject("World");
            var wb = go.AddComponent<WorldBuilder>();
            var rng = new System.Random(System.Environment.TickCount);

            int bx = 0, by = 0;
            Side entrySide = Side.None; int entryAt = 0;
            Side nextExit = rng.Next(2) == 0 ? Side.NE : Side.NW;

            for (int i = 1; i <= blockCount; i++)
            {
                var size = i == 1 ? Sizes[0] : Sizes[rng.Next(Sizes.Length)];
                int gx = size.x, gy = size.y;
                bool last = i == blockCount;
                Side exitSide = last ? Side.None : nextExit;

                // 上一个街区已经定好了连接段的终点；用入口位置反推这个街区的原点
                if (entrySide == Side.NE) { entryAt = 2 + rng.Next(gx - 4 - RoomDirector.GateWidth); bx -= entryAt; by -= gy; }
                if (entrySide == Side.NW) { entryAt = 2 + rng.Next(gy - 4 - RoomDirector.GateWidth); by -= entryAt; bx -= gx; }

                int exitAt = 0;
                if (exitSide == Side.NE) exitAt = 2 + rng.Next(gx - 4 - RoomDirector.GateWidth);
                if (exitSide == Side.NW) exitAt = 2 + rng.Next(gy - 4 - RoomDirector.GateWidth);

                var block = RoomDirector.Build(go.transform, i, last, bx, by, gx, gy, entrySide, entryAt, exitSide, exitAt, player, rng);
                wb.Blocks.Add(block);
                if (last) break;

                // 连接段：3 格宽的街道，从出口缺口一直通到下一个街区的入口
                int len = 6 + rng.Next(5);
                if (exitSide == Side.NE)
                {
                    int sx = bx + exitAt;
                    wb.Street(sx, by - len, sx + RoomDirector.GateWidth, by, alongY: true);
                    bx = sx; by = by - len;                 // 下一个街区的入口缺口左端在 (bx, by)
                }
                else
                {
                    int sy = by + exitAt;
                    wb.Street(bx - len, sy, bx, sy + RoomDirector.GateWidth, alongY: false);
                    by = sy; bx = bx - len;
                }
                entrySide = exitSide;
                nextExit = exitSide == Side.NE ? Side.NW : Side.NE;      // 左右交替，路线呈之字形向上
            }
            return wb;
        }

        void Street(int x0, int y0, int x1, int y1, bool alongY)
        {
            var t = transform;
            Iso.Poly(t, "Street", new[] { Iso.W(x0, y0), Iso.W(x1, y0), Iso.W(x1, y1), Iso.W(x0, y1) }, StreetCol, -30000);
            var host = new GameObject("StreetBounds");
            host.transform.SetParent(t, false);
            if (alongY)
            {
                // 后侧（x 小的一边）是矮墙，前侧（x 大的一边）是护栏
                Iso.Edge(host, new Vector2(x0, y0), new Vector2(x0, y1));
                Iso.Edge(host, new Vector2(x1, y0), new Vector2(x1, y1));
                for (int j = y0; j < y1; j++)
                {
                    float top = Mathf.Max(Iso.W(x0, j).y, Iso.W(x0, j + 1).y);
                    Iso.Face(t, "StreetWall", new Vector2(x0, j + 1), new Vector2(x0, j), 0.9f, WallCol, Iso.Order(top + 0.05f));
                    float low = Mathf.Min(Iso.W(x1, j).y, Iso.W(x1, j + 1).y);
                    Iso.Face(t, "StreetRail", new Vector2(x1, j), new Vector2(x1, j + 1), 0.06f, RailCol, Iso.Order(low - 0.05f), 0.3f);
                }
                for (int j = y0; j < y1; j += 2)             // 路面中线
                    Iso.Poly(t, "Lane", new[] { Iso.W(x0 + 1.45f, j), Iso.W(x0 + 1.55f, j), Iso.W(x0 + 1.55f, j + 1), Iso.W(x0 + 1.45f, j + 1) }, Sprites.Hex("8a7a3a"), -29990);
            }
            else
            {
                Iso.Edge(host, new Vector2(x0, y0), new Vector2(x1, y0));
                Iso.Edge(host, new Vector2(x0, y1), new Vector2(x1, y1));
                for (int i = x0; i < x1; i++)
                {
                    float top = Mathf.Max(Iso.W(i, y0).y, Iso.W(i + 1, y0).y);
                    Iso.Face(t, "StreetWall", new Vector2(i, y0), new Vector2(i + 1, y0), 0.9f, WallCol, Iso.Order(top + 0.05f));
                    float low = Mathf.Min(Iso.W(i, y1).y, Iso.W(i + 1, y1).y);
                    Iso.Face(t, "StreetRail", new Vector2(i, y1), new Vector2(i + 1, y1), 0.06f, RailCol, Iso.Order(low - 0.05f), 0.3f);
                }
                for (int i = x0; i < x1; i += 2)
                    Iso.Poly(t, "Lane", new[] { Iso.W(i, y0 + 1.45f), Iso.W(i + 1, y0 + 1.45f), Iso.W(i + 1, y0 + 1.55f), Iso.W(i, y0 + 1.55f) }, Sprites.Hex("8a7a3a"), -29990);
            }
        }
    }
}
