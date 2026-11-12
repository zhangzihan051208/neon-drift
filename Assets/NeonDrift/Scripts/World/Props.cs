using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonDrift
{
    /// <summary>
    /// 街区里的物件：StreamingAssets/Props 下按文件夹分类，每张 png 配一个同名 json。
    /// covers 掩体（挡子弹）· props 杂物 · decals 地面贴花 · unique 只摆一次的（零的车、电话亭）。
    /// 支点在物件落地的最前角，所以物件立在格子 (x, y) 时，占的是 (x - foot, y - foot) 到 (x, y) 这一块。
    /// </summary>
    public static class Props
    {
        [System.Serializable]
        class Meta { public float pivotX = 0.5f; public float pivotY = 0f; public float foot = 0f; public string light = ""; public string unique = ""; public float fireX = 0f; public float fireY = -1f; }

        public class Entry { public string name; public Sprite sprite; public float foot; public string light; public string unique; public float footX, footY; public Vector2 fire = new Vector2(0f, -1f); public Glow.Info glow; }

        /// <summary>
        /// 从图的底部轮廓算出物件实际占地（格）：支点（最低点）是前角，
        /// 底部那一圈最左的点是沿 -x 的角，最右的点是沿 -y 的角。屏幕上 32 像素 = 1 格。
        /// </summary>
        public static Vector2 Footprint(Texture2D tex, float pivotX, float shrink = 0.85f, bool full = false)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int bottom = -1;
            for (int y = 0; y < h && bottom < 0; y++)
                for (int x = 0; x < w; x++) if (px[y * w + x].a > 0) { bottom = y; break; }
            if (bottom < 0) return Vector2.zero;
            int band = full ? h : Mathf.Min(Mathf.RoundToInt(h * 0.4f), w / 4 + 3);    // 房子很宽，底边会一直斜上去，要看整张图
            int minX = w, maxX = -1;
            for (int y = bottom; y < Mathf.Min(h, bottom + band); y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 0) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
            float fx = pivotX * w;
            float ppt = Iso.HW * Sprites.PPU;          // 一格在屏幕上的横向像素
            return new Vector2(Mathf.Max(0.3f, (fx - minX) / ppt * shrink), Mathf.Max(0.3f, (maxX - fx) / ppt * shrink));
        }

        static Dictionary<string, List<Entry>> cats;

        public static List<Entry> Get(string cat)
        {
            Load();
            return cats.TryGetValue(cat, out var l) ? l : new List<Entry>();
        }

        public static Entry Find(string cat, string name) => Get(cat).Find(e => e.name == name);

        static void Load()
        {
            if (cats != null) return;
            cats = new Dictionary<string, List<Entry>>();
            string root = Path.Combine(Application.streamingAssetsPath, "Props");
            if (!Directory.Exists(root)) return;
            int n = 0;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var list = new List<Entry>();
                foreach (var png in Directory.GetFiles(dir, "*.png"))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    if (!tex.LoadImage(File.ReadAllBytes(png))) continue;
                    var meta = new Meta();
                    string js = Path.ChangeExtension(png, ".json");
                    if (File.Exists(js)) JsonUtility.FromJsonOverwrite(File.ReadAllText(js), meta);
                    var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(meta.pivotX, meta.pivotY), Sprites.PPU, 0, SpriteMeshType.FullRect);
                    string nm = Path.GetFileNameWithoutExtension(png);
                    sp.name = nm;
                    var fp = meta.pivotY > 0.5f ? Vector2.zero : Footprint(tex, meta.pivotX);
                    list.Add(new Entry { name = nm, sprite = sp, foot = meta.foot, light = meta.light ?? "", unique = meta.unique ?? "", footX = fp.x, footY = fp.y, fire = new Vector2(meta.fireX, meta.fireY) / Sprites.PPU,
                                         glow = Path.GetFileName(dir) == "decals" ? null : Glow.Extract(tex, new Vector2(meta.pivotX, meta.pivotY), 1) });
                    n++;
                }
                cats[Path.GetFileName(dir)] = list;
            }
            Debug.Log($"物件素材：读到 {n} 张");
        }

        /// <summary>立一个物件。solid = 有碰撞（掩体、车、电话亭）。</summary>
        public static GameObject Place(Transform parent, Entry e, Vector2 tile, bool flip, bool solid, bool shadow = true)
        {
            var go = new GameObject(e.name);
            go.transform.SetParent(parent, true);
            Vector2 p = Iso.W(tile.x, tile.y);
            go.transform.position = p;
            var sr = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(go.transform, false);
            sr.sprite = e.sprite;
            sr.flipX = flip;
            sr.sharedMaterial = Sprites.Unlit;          // 图里已经画好夜景的光
            if (solid && e.footX > 0f) AddFoot(go, tile, flip ? e.footY : e.footX, flip ? e.footX : e.footY);
            go.AddComponent<SortingGroup>();
            go.AddComponent<YSort>().isStatic = true;
            if (shadow && e.footX > 0f) Shadow(parent, tile, flip ? e.footY : e.footX, flip ? e.footX : e.footY);
            // 发光部分（路灯灯罩、售货机屏幕、霓虹牌、电话亭、车灯、电网）；自己已经带灯的（路灯、火桶）不再加灯
            if (e.glow != null)
                Glow.Attach(go.transform, parent, e.glow, flip, 10, string.IsNullOrEmpty(e.light) ? 1 : 0, null,
                            new System.Random(Mathf.RoundToInt(tile.x * 97 + tile.y * 31)), flicker: e.unique == "");
            return go;
        }

        static Sprite shadowSprite;

        /// <summary>
        /// 脚下一圈柔和的接地阴影，盖在地面上、压在所有物件和角色下面，让东西看起来是压在地上的，而不是贴在画面上。
        /// 不挂在物件下面（物件有排序组，会把阴影一起排到前面去）。
        /// </summary>
        public static void Shadow(Transform parent, Vector2 front, float fx, float fy, float strength = 0.5f)
        {
            if (shadowSprite == null)
            {
                const int W = 64, H = 32;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float a = Mathf.Clamp01(1f - r);
                        px[y * W + x] = new Color32(0, 0, 0, (byte)(255 * a * a * (3f - 2f * a)));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                shadowSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
            }
            var sr = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, true);
            sr.transform.position = Iso.W(front.x - fx * 0.5f, front.y - fy * 0.5f);
            float w = (fx + fy) * Iso.HW * 1.35f + 0.3f;                  // 比占地稍大一圈
            sr.transform.localScale = new Vector3(w / 2f, w / 2f, 1f);     // 贴图本身是 2×1 单位，正好是斜 45° 的扁椭圆
            sr.sprite = shadowSprite;
            sr.sharedMaterial = Sprites.Unlit;
            sr.color = new Color(0f, 0f, 0f, strength);
            sr.sortingOrder = -29975;
        }

        /// <summary>按占地加一个菱形碰撞：front 是前角，沿 -x 占 fx 格、沿 -y 占 fy 格。</summary>
        public static void AddFoot(GameObject go, Vector2 front, float fx, float fy)
        {
            Vector2 p = go.transform.position;
            var col = go.AddComponent<PolygonCollider2D>();
            col.points = new[] { Iso.W(front.x - fx, front.y) - p, Iso.W(front.x - fx, front.y - fy) - p, Iso.W(front.x, front.y - fy) - p, Vector2.zero };
        }

        /// <summary>贴在地上的东西（水坑、井盖、烟头），永远在角色下面。</summary>
        public static void Decal(Transform parent, Entry e, Vector2 tile, bool flip, int order)
        {
            var sr = new GameObject(e.name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, true);
            sr.transform.position = Iso.W(tile.x, tile.y);
            sr.sprite = e.sprite;
            sr.flipX = flip;
            sr.sharedMaterial = Sprites.Unlit;
            sr.sortingOrder = order;
        }
    }
}
