using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 房子素材：StreamingAssets/Buildings 里的 png，每张配一个同名 json（前角位置和正面占几格）。
    /// 图里房子的正面朝左下；放在左上那面墙后面时左右翻转。
    /// </summary>
    public static class Buildings
    {
        [System.Serializable]
        class Meta { public float pivotX = 0.5f; public float pivotY = 0f; public int facadeTiles = 4; public string unique = ""; }

        /// <summary>unique 为空的随机摆；"start" 只在第一个街区出生点旁边摆一次（回声诊所）。</summary>
        public class Entry { public Sprite sprite; public int tiles; public string unique; public float footX, footY; public float height; }

        /// <summary>后墙和房子用的排序区间：比地面高，比所有角色和物件低。</summary>
        public const int BackLayer = -20000;

        static List<Entry> all;

        public static List<Entry> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Entry>();
                string dir = Path.Combine(Application.streamingAssetsPath, "Buildings");
                if (!Directory.Exists(dir)) return all;
                foreach (var png in Directory.GetFiles(dir, "*.png"))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    if (!tex.LoadImage(File.ReadAllBytes(png))) continue;
                    var meta = new Meta();
                    string js = Path.ChangeExtension(png, ".json");
                    if (File.Exists(js)) JsonUtility.FromJsonOverwrite(File.ReadAllText(js), meta);
                    var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(meta.pivotX, meta.pivotY), Sprites.PPU, 0, SpriteMeshType.FullRect);
                    sp.name = Path.GetFileNameWithoutExtension(png);
                    var fp = Props.Footprint(tex, meta.pivotX, 0.95f, full: true);
                    all.Add(new Entry { sprite = sp, tiles = Mathf.Max(1, meta.facadeTiles), unique = meta.unique ?? "", footX = fp.x, footY = fp.y, height = tex.height / (float)Sprites.PPU });
                }
                Debug.Log($"房子素材：读到 {all.Count} 张");
                return all;
            }
        }

        /// <summary>把一栋房子立在墙线后面。front 是房子正面最靠前的那个角（格子坐标）。</summary>
        public static void Place(Transform parent, Entry e, Vector2 frontTile, bool flip, System.Random rng)
        {
            var go = new GameObject("House_" + e.sprite.name);
            go.transform.SetParent(parent, true);
            var p = Iso.W(frontTile.x, frontTile.y);
            go.transform.position = p;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = e.sprite;
            sr.flipX = flip;
            sr.sharedMaterial = Sprites.Unlit;          // 图里已经画好了夜景的光
            float v = 0.85f + (float)rng.NextDouble() * 0.15f;
            sr.color = new Color(v, v, v * 1.02f, 1f);
            sr.sortingOrder = BackLayer + Iso.Order(p.y) + 3;     // 房子永远在角色后面
            go.AddComponent<Occluder>().sr = sr;                 // 角色走到房子后面时房子变半透明
            // 房子的碰撞：看到房子底座的地方就走不进去（正面朝左下时沿 -x 是正面，翻转后沿 -y 是正面）
            Props.AddFoot(go, frontTile, flip ? e.footY : e.footX, flip ? e.footX : e.footY);
        }
    }

    /// <summary>角色走到房子后面、被房子挡住时，房子变半透明，离开后恢复。</summary>
    public class Occluder : MonoBehaviour
    {
        public SpriteRenderer sr;
        const float Faded = 0.3f;
        float a = 1f;

        void Update()
        {
            if (sr == null) return;
            var pl = GameRoot.I != null ? GameRoot.I.Player : null;
            float target = 1f;
            if (pl != null)
            {
                Vector2 p = (Vector2)pl.transform.position + Vector2.up * 1f;     // 角色身体中间
                var b = sr.bounds;
                bool inside = p.x > b.min.x && p.x < b.max.x && p.y > b.min.y && p.y < b.max.y;
                if (inside && p.y > transform.position.y + 0.3f) target = Faded;   // 在房子前角以上 = 在房子后面
            }
            if (Mathf.Approximately(a, target)) return;
            a = Mathf.MoveTowards(a, target, Time.deltaTime * 4f);
            var c = sr.color; c.a = a; sr.color = c;
        }
    }
}
