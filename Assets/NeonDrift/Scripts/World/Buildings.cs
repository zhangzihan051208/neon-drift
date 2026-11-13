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
        class Meta { public float pivotX = 0.5f; public float pivotY = 0f; public int facadeTiles = 4; public string unique = ""; public Props.WindRect[] wind; }

        /// <summary>房子整体放大的倍数：人物放大后，原来的门只到人物的腰，放大 1.5 倍后门和人差不多高。</summary>
        public const float Scale = 1.3f;

        /// <summary>unique 为空的随机摆；"start" 只在第一个街区出生点旁边摆一次（回声诊所）。</summary>
        public class Entry { public Sprite sprite; public int tiles; public string unique; public float footX, footY; public float height; public Glow.Info glow; public Props.WindRect[] wind; public Texture2D tex; public Vector2 pivot; }

        /// <summary>后墙和房子用的排序区间：比地面高，比所有角色和物件低。</summary>
        public const int BackLayer = -20000;

        static List<Entry> all;

        [System.Serializable] class SignMeta { public string building; public string file; public float dx; public float dy; public string style; }
        [System.Serializable] class SignList { public SignMeta[] items; }
        public class Sign { public Sprite left, right; public Glow.Info glowL, glowR; public Vector2 offset; public string style; }
        static Dictionary<string, List<Sign>> signs;

        /// <summary>灯牌：StreamingAssets/Signs，每块两张（L 贴在不翻转的房子上，R 贴在翻转的房子上，字都是正的）。</summary>
        static List<Sign> SignsOf(string building)
        {
            if (signs == null)
            {
                signs = new Dictionary<string, List<Sign>>();
                string dir = Path.Combine(Application.streamingAssetsPath, "Signs");
                string js = Path.Combine(dir, "signs.json");
                if (File.Exists(js))
                {
                    var list = JsonUtility.FromJson<SignList>("{\"items\":" + File.ReadAllText(js) + "}");
                    foreach (var m in list.items)
                    {
                        var s = new Sign { offset = new Vector2(m.dx, m.dy) / Sprites.PPU, style = m.style };
                        s.left = LoadSign(Path.Combine(dir, m.file + "_L.png"), out s.glowL);
                        s.right = LoadSign(Path.Combine(dir, m.file + "_R.png"), out s.glowR);
                        if (s.left == null || s.right == null) continue;
                        if (!signs.TryGetValue(m.building, out var l)) signs[m.building] = l = new List<Sign>();
                        l.Add(s);
                    }
                }
            }
            return signs.TryGetValue(building, out var r) ? r : null;
        }

        static Sprite LoadSign(string path, out Glow.Info glow)
        {
            glow = null;
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
            glow = Glow.Extract(tex, new Vector2(0.5f, 0.5f), 1, strict: false);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
        }

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
                    var fp = Props.Footprint(tex, meta.pivotX, 0.95f, full: true) * Scale;
                    all.Add(new Entry { sprite = sp, tiles = Mathf.Max(1, Mathf.CeilToInt(meta.facadeTiles * Scale)), unique = meta.unique ?? "", footX = fp.x, footY = fp.y, height = tex.height / (float)Sprites.PPU * Scale,
                                        wind = meta.wind, tex = tex, pivot = new Vector2(meta.pivotX, meta.pivotY), glow = Glow.Extract(tex, new Vector2(meta.pivotX, meta.pivotY)) });
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
            var art = new GameObject("Art");                       // 图和挂在图上的东西（发光层、灯牌、风吹的局部）一起放大
            art.transform.SetParent(go.transform, false);
            art.transform.localScale = new Vector3(Scale, Scale, 1f);
            var sr = art.AddComponent<SpriteRenderer>();
            sr.sprite = e.sprite;
            sr.flipX = flip;
            sr.sharedMaterial = Sprites.Unlit;          // 图里已经画好了夜景的光
            float v = 0.85f + (float)rng.NextDouble() * 0.15f;
            sr.color = new Color(v, v, v * 1.02f, 1f);
            sr.sortingOrder = BackLayer + Iso.Order(p.y) + 3;     // 房子永远在角色后面
            var occ = go.AddComponent<Occluder>();               // 角色走到房子后面时房子变半透明
            occ.sr = sr;
            // 窗户、招牌发光，倒映在湿地上，照亮门口的地面；回声诊所的招牌是整条街唯一不闪的
            Wind.Attach(art.transform, e.tex, e.pivot, e.wind, flip, sr.sortingOrder + 1, sr.color, occ.extra);
            Glow.Attach(art.transform, parent, e.glow, flip, sr.sortingOrder + 2, 2, occ.extra, rng, flicker: e.unique != "start", scale: Scale);
            occ.front = frontTile;
            // 霓虹灯牌：高低错落（门头、墙角竖招牌、屋顶），让人一眼看出这是什么店
            var list = SignsOf(e.sprite.name);
            if (list != null)
                for (int si = 0; si < list.Count; si++)
                {
                    var s = list[si];
                    // 不要太整齐：次要的招牌有时不挂；每块上下左右随机错开几个像素（贴墙的沿墙面错开，伸出的只上下错开）
                    if (si > 0 && e.unique != "start" && rng.NextDouble() < 0.2) continue;
                    float jx = s.style == "facade" ? (float)(rng.NextDouble() * 2 - 1) * 4f : 0f;
                    float jy = (float)(rng.NextDouble() * 2 - 1) * (s.style == "roof" ? 1f : 6f);
                    Vector2 off = s.offset + new Vector2(jx, jy - jx * 0.5f) / Sprites.PPU;     // 沿正面错开时顺着 2:1 的斜度
                    var sg = new GameObject("Sign").AddComponent<SpriteRenderer>();
                    sg.transform.SetParent(art.transform, false);
                    sg.transform.localPosition = new Vector3(flip ? -off.x : off.x, off.y, 0f);
                    sg.sprite = flip ? s.right : s.left;
                    sg.sharedMaterial = Sprites.Unlit;
                    sg.sortingOrder = sr.sortingOrder + 3;
                    if (s.style == "blade") sg.gameObject.AddComponent<SignSway>();
                    occ.extra.Add(sg);
                    Glow.Attach(sg.transform, parent, flip ? s.glowR : s.glowL, false, sr.sortingOrder + 4, 0, occ.extra, rng,
                                flicker: e.unique != "start", reflect: false);
                    // 低处的招牌照亮门口的地面，并在湿地上留一道倒影
                    var gi = flip ? s.glowR : s.glowL;
                    if (gi != null && off.y * Scale < 7.5f)
                    {
                        Vector2 foot = (Vector2)go.transform.position + new Vector2((flip ? -off.x : off.x) * Scale, 0.4f);
                        var c = gi.colors.Count > 0 ? gi.colors[0] : Color.white;
                        Atmosphere.Lamp(go.transform, foot, c, 2.6f, 0.6f);
                    }
                }
            // 房子的碰撞：看到房子底座的地方就走不进去（正面朝左下时沿 -x 是正面，翻转后沿 -y 是正面）
            Props.AddFoot(go, frontTile, flip ? e.footY : e.footX, flip ? e.footX : e.footY);
        }
    }

    /// <summary>角色走到房子后面、被房子挡住时，房子变半透明，离开后恢复。</summary>
    public class Occluder : MonoBehaviour
    {
        public SpriteRenderer sr;
        public Vector2 front;                                                       // 房子地基最前面那个角（格子坐标）
        public readonly List<SpriteRenderer> extra = new List<SpriteRenderer>();   // 发光层、倒影，跟着一起淡
        const float Faded = 0.3f;
        float a = 1f;

        void Update()
        {
            if (sr == null) return;
            var pl = GameRoot.I != null ? GameRoot.I.Player : null;
            float target = 1f;
            if (pl != null)
            {
                // 只有角色真的走到房子地基后面（比如出口外那条街）才淡出；在房子前面怎么贴近都不透明
                Vector2 p = (Vector2)pl.transform.position + Vector2.up * 1f;
                var b = sr.bounds;
                bool inside = p.x > b.min.x && p.x < b.max.x && p.y > b.min.y && p.y < b.max.y;
                Vector2 tile = Iso.Tile(pl.transform.position);
                bool behind = tile.x < front.x - 0.3f && tile.y < front.y - 0.3f;
                if (inside && behind) target = Faded;
            }
            if (Mathf.Approximately(a, target)) return;
            a = Mathf.MoveTowards(a, target, Time.deltaTime * 4f);
            var c = sr.color; c.a = a; sr.color = c;
            for (int i = 0; i < extra.Count; i++)
            {
                if (extra[i] == null) continue;
                var ec = extra[i].color; ec.a = extra[i].name == "Reflection" ? 0.18f * a : a; extra[i].color = ec;
            }
        }
    }
}
