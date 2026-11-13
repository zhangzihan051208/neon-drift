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
            new Vector2Int(27, 24), new Vector2Int(30, 22), new Vector2Int(25, 25), new Vector2Int(32, 20), new Vector2Int(22, 28),      // 人物和房子都放大了，街区整体加大约三分之一
        };

        static readonly Color StreetCol = Sprites.Hex("242c3a");
        static readonly Color RailCol = Sprites.Hex("b5622e");
        static readonly Color WallCol = Sprites.Hex("2c3442");

        public static WorldBuilder Build(PlayerController player, int blockCount = 5)
        {
            var go = new GameObject("World");
            var wb = go.AddComponent<WorldBuilder>();
            var rng = new System.Random(System.Environment.TickCount);

            int bx = 0, by = 0;
            Side entrySide = Side.None; int entryAt = 0;
            Side nextExit = Side.NE;      // 第一个街区固定往右上出，左上墙留给回声诊所

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
                int len = Mathf.Max(6 + rng.Next(5), (exitSide == Side.NE ? block.BackNE : block.BackNW) + 4);
                if (exitSide == Side.NE)
                {
                    int sx = bx + exitAt;
                    wb.Street(sx, by - len, sx + RoomDirector.GateWidth, by, alongY: true, inner: block.BackNE);
                    bx = sx; by = by - len;                 // 下一个街区的入口缺口左端在 (bx, by)
                }
                else
                {
                    int sy = by + exitAt;
                    wb.Street(bx - len, sy, bx, sy + RoomDirector.GateWidth, alongY: false, inner: block.BackNW);
                    by = sy; bx = bx - len;
                }
                entrySide = exitSide;
                nextExit = exitSide == Side.NE ? Side.NW : Side.NE;      // 左右交替，路线呈之字形向上
            }
            return wb;
        }

        /// <summary>
        /// 连接段：和门一样宽的街道（RoomDirector.GateWidth 格），从出口一直通到下一个街区的入口。
        /// 起点那 inner 格穿过上一个街区放房子的那圈平台，那里不画护栏和平台边；其余部分两侧都围上护栏。
        /// </summary>
        void Street(int x0, int y0, int x1, int y1, bool alongY, int inner)
        {
            var t = transform;
            Iso.Ground(t, "Street", x0, y0, x1, y1, StreetCol, -30000);
            var host = new GameObject("StreetBounds");
            host.transform.SetParent(t, false);
            var prng = new System.Random(x0 * 31 + y0 * 17);
            if (alongY)
            {
                Iso.Edge(host, new Vector2(x0, y0), new Vector2(x0, y1));
                Iso.Edge(host, new Vector2(x1, y0), new Vector2(x1, y1));
                if (y1 - inner > y0)
                {
                    Platform.Edge(t, new Vector2(x1, y0), new Vector2(x1, y1 - inner), prng);
                    Platform.Fence(t, new Vector2(x1, y0), new Vector2(x1, y1 - inner), false, prng);    // 朝镜头那侧
                    Platform.Fence(t, new Vector2(x0, y0), new Vector2(x0, y1 - inner), true, prng);     // 背面那侧
                }
                for (int j = y0; j < y1; j += 2)             // 路面中线
                {
                    float m = (x0 + x1) * 0.5f;                  // 路面中线放在正中
                    Iso.Poly(t, "Lane", new[] { Iso.W(m - 0.05f, j), Iso.W(m + 0.05f, j), Iso.W(m + 0.05f, j + 1), Iso.W(m - 0.05f, j + 1) }, Sprites.Hex("8a7a3a"), -29990);
                }
            }
            else
            {
                Iso.Edge(host, new Vector2(x0, y0), new Vector2(x1, y0));
                Iso.Edge(host, new Vector2(x0, y1), new Vector2(x1, y1));
                if (x1 - inner > x0)
                {
                    Platform.Edge(t, new Vector2(x0, y1), new Vector2(x1 - inner, y1), prng);
                    Platform.Fence(t, new Vector2(x0, y1), new Vector2(x1 - inner, y1), false, prng);
                    Platform.Fence(t, new Vector2(x0, y0), new Vector2(x1 - inner, y0), true, prng);
                }
                for (int i = x0; i < x1; i += 2)
                {
                    float m = (y0 + y1) * 0.5f;
                    Iso.Poly(t, "Lane", new[] { Iso.W(i, m - 0.05f), Iso.W(i + 1, m - 0.05f), Iso.W(i + 1, m + 0.05f), Iso.W(i, m + 0.05f) }, Sprites.Hex("8a7a3a"), -29990);
                }
            }
        }
    }
}
