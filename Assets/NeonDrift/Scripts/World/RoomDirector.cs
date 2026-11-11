using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 灰盒房间：生成墙、掩体、积水（装饰），按波次刷怪；清完后开门并给三选一奖励。
    /// 1 格 = 1 单位 = 32 像素。标准房间 30×17 格正好一屏。
    /// </summary>
    public class RoomDirector : MonoBehaviour
    {
        public Rect Bounds { get; private set; }
        public int Wave { get; private set; }
        public int WaveCount { get; private set; }
        public int Alive { get; private set; }
        public bool Cleared { get; private set; }

        int index, w, h;
        PlayerController player;
        SpriteRenderer door;
        Collider2D doorCol;
        readonly List<Rect> blocked = new List<Rect>();

        static readonly Vector2Int[] Sizes = { new Vector2Int(30, 17), new Vector2Int(40, 22), new Vector2Int(34, 20) };

        static readonly Color FloorCol = Sprites.Hex("1c2024");
        static readonly Color WallCol = Sprites.Hex("2e3236");
        static readonly Color WallTop = Sprites.Hex("464b50");
        static readonly Color WaterCol = Sprites.Hex("10182a");

        public static RoomDirector Build(int index, PlayerController player)
        {
            var go = new GameObject($"Room {index}");
            var rd = go.AddComponent<RoomDirector>();
            rd.index = index;
            rd.player = player;
            var s = Sizes[(index - 1) % Sizes.Length];
            rd.w = s.x; rd.h = s.y;
            rd.Bounds = new Rect(0, 0, rd.w, rd.h);
            rd.Generate();
            return rd;
        }

        void Generate()
        {
            var rng = new System.Random(index * 7919 + System.Environment.TickCount);

            // 地板（平铺格子，方便看清尺寸）
            var floor = Sprites.Make(transform, "Floor", Sprites.FloorTile(), FloorCol, -30000, Vector2.zero);
            floor.drawMode = SpriteDrawMode.Tiled;
            floor.size = new Vector2(w, h);

            // 积水（装饰，机制在下一步加入）
            for (int i = 0; i < 3; i++)
            {
                float px = 5 + (float)rng.NextDouble() * (w - 10), py = 3 + (float)rng.NextDouble() * (h - 7);
                var pud = Sprites.Make(transform, "Puddle", Sprites.Circle(64), WaterCol, -29000, new Vector2(px, py));
                pud.transform.localScale = new Vector3(1.6f + (float)rng.NextDouble() * 1.4f, 0.7f + (float)rng.NextDouble() * 0.5f, 1f);
            }

            // 外墙：上墙 2 格高（俯视角能看到墙面），其余 1 格
            int doorY0 = h / 2 - 1, doorY1 = h / 2 + 2;
            Wall(0, 0, w, 1);
            Wall(0, h - 2, w, 2, face: true);
            Wall(0, 1, 1, h - 3);
            Wall(w - 1, 1, 1, doorY0 - 1);
            Wall(w - 1, doorY1, 1, h - 2 - doorY1);
            // 门（右墙中间 3 格）
            door = Sprites.Make(transform, "Door", Sprites.Box(32, 96, feet: false), Sprites.Hex("e43b44"), 0, new Vector2(w - 0.5f, (doorY0 + doorY1) / 2f));
            var dc = door.gameObject.AddComponent<BoxCollider2D>();
            dc.size = new Vector2(1f, doorY1 - doorY0);
            door.transform.localScale = new Vector3(1f, (doorY1 - doorY0) / 3f, 1f);
            doorCol = dc;

            // 掩体柱子
            blocked.Add(new Rect(0, h / 2f - 3, 6, 6)); // 玩家出生区留空
            int pillars = 3 + index % 3;
            for (int i = 0, tries = 0; i < pillars && tries < 200; tries++)
            {
                int px = 4 + rng.Next(w - 8), py = 3 + rng.Next(h - 7);
                var r = new Rect(px - 1, py - 1, 4, 4);
                bool overlap = false;
                foreach (var b in blocked) if (b.Overlaps(r)) { overlap = true; break; }
                if (overlap) continue;
                blocked.Add(r);
                Wall(px, py, 2, 2, face: true);
                i++;
            }

            // 玩家位置
            player.transform.position = new Vector2(2.5f, h / 2f);
            player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

            WaveCount = Mathf.Min(4, 2 + (index - 1) / 2);
            StartCoroutine(RunWaves());
            GreyboxHUD.Message(index == 1 ? "WASD 移动 · 鼠标瞄准 · 左键射击 · 空格闪移\n子弹能在墙上反弹一次" : $"房间 {index}", 3f);
        }

        void Wall(float x, float y, float ww, float hh, bool face = false)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector2(x + ww / 2f, y + hh / 2f);
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(ww, hh);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprites.Box(32, 32);
            sr.sharedMaterial = Sprites.Unlit;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(ww, hh);
            sr.color = WallCol;
            sr.sortingOrder = 1000 - Mathf.RoundToInt(y * 20f);
            if (face)
            {
                // 顶面颜色更亮，模拟俯视角看到的墙顶
                var top = Sprites.Make(go.transform, "Top", Sprites.Box(32, 32), WallTop, sr.sortingOrder + 1, new Vector2(0f, hh / 2f - 0.25f));
                top.drawMode = SpriteDrawMode.Sliced;
                top.size = new Vector2(ww, 0.5f);
            }
        }

        IEnumerator RunWaves()
        {
            yield return new WaitForSeconds(1.0f);
            var rng = new System.Random(index * 31 + System.Environment.TickCount);
            for (Wave = 1; Wave <= WaveCount; Wave++)
            {
                int n = 3 + index + (Wave - 1);
                var spots = new List<Vector2>();
                for (int tries = 0; spots.Count < n && tries < 400; tries++)
                {
                    var p = new Vector2(2 + (float)rng.NextDouble() * (w - 4), 2 + (float)rng.NextDouble() * (h - 5));
                    if (Vector2.Distance(p, player.transform.position) < 6f) continue;
                    if (Physics2D.OverlapCircle(p + Vector2.up * 0.3f, 0.7f) != null) continue;
                    bool close = false;
                    foreach (var s in spots) if (Vector2.Distance(s, p) < 1.5f) close = true;
                    if (!close) spots.Add(p);
                }
                var markers = new List<GameObject>();
                foreach (var s in spots) markers.Add(Effects.SpawnMarker(s + Vector2.up * 0.2f, new Color(1f, 0.2f, 0.3f, 0.5f)));
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
            door.color = Sprites.Hex("3effa0");
            doorCol.enabled = false;
            var c = new Vector2(w / 2f, h / 2f);
            var pool = new List<RewardType> { RewardType.FireRate, RewardType.Bounce, RewardType.MaxHp, RewardType.Damage };
            for (int i = 0; i < 3; i++)
            {
                var r = pool[Random.Range(0, pool.Count)];
                pool.Remove(r);
                Pickup.Spawn(transform, c + new Vector2((i - 1) * 3f, 0f), r);
            }
            GreyboxHUD.Message("房间清理完毕：三选一奖励，然后走向右边的门", 4f);
        }

        void Update()
        {
            if (Cleared && player != null && !player.IsDead && player.transform.position.x > w - 0.6f)
                GameRoot.I.NextRoom();
        }
    }
}
