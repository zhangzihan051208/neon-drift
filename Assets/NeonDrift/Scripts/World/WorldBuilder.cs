using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 一章的第一部分：4 间房，一次只存在一间。房间之间不连通、互相看不到，清完后爬后墙上的梯子转场。
    /// 第 2 或第 3 间打完会出现岔路（三把梯子）：继续小怪房，或者改去黑市 / 随机事件（顶替掉下一间小怪房）。
    /// 第 4 间打完给 AI 协议三选一，梯子通往 Boss 战。
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        public const int Part1Rooms = 4;

        public RoomDirector Current { get; private set; }
        public int Step { get; private set; }          // 第几间（1–4）
        public int ForkAt { get; private set; }        // 哪一间打完出现岔路

        static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(27, 24), new Vector2Int(30, 22), new Vector2Int(25, 25), new Vector2Int(32, 20), new Vector2Int(22, 28),
        };
        static readonly Vector2Int SmallSize = new Vector2Int(22, 20);      // 黑市和事件房小一点

        PlayerController player;
        System.Random rng;

        public static WorldBuilder Build(PlayerController player)
        {
            var go = new GameObject("World");
            var wb = go.AddComponent<WorldBuilder>();
            wb.player = player;
            wb.rng = new System.Random(System.Environment.TickCount);
            wb.ForkAt = 2 + wb.rng.Next(2);             // 岔路放在第 2 或第 3 间之后：那时身上才攒得够买东西的信用点
            wb.Next(RoomKind.Combat);
            return wb;
        }

        /// <summary>拆掉当前这间，在同一个位置生成下一间。</summary>
        public void Next(RoomKind kind)
        {
            if (Current != null) { Current.gameObject.SetActive(false); Destroy(Current.gameObject); }
            Projectile.ClearAll();
            Step++;
            var size = kind != RoomKind.Combat ? SmallSize : Step == 1 ? Sizes[0] : Sizes[rng.Next(Sizes.Length)];
            bool last = Step >= Part1Rooms;
            RoomKind[] exits = last ? new[] { RoomKind.Boss }
                             : Step == ForkAt ? new[] { RoomKind.Combat, RoomKind.Shop, RoomKind.Event, RoomKind.Signal }
                             : new[] { RoomKind.Combat };
            Current = RoomDirector.Build(transform, Step, last, 0, 0, size.x, size.y, Side.None, 0, Side.None, 0, player, rng, kind, exits);
        }
    }
}
