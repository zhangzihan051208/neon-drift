using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 让图里本来就亮的东西（窗户、招牌、灯笼、火光、霓虹）真正发光：
    /// 1. 发光层：把亮的像素单独拿出来，叠加、超过 1 的亮度，交给泛光晕开；
    /// 2. 湿地倒影：发光层上下翻转、压扁、调淡，铺在地面上；
    /// 3. 照到地上的光：在最亮的几处放 2D 灯，地面会被染上颜色。
    /// </summary>
    public static class Glow
    {
        /// <summary>从一张图里提取出来的发光信息。</summary>
        public class Info
        {
            public Sprite sprite;                        // 只有发光像素的那一层，支点和原图一样
            public List<Vector3> spots = new List<Vector3>();   // 最亮的几处：x、y 是相对支点的单位坐标，z 是权重
            public List<Color> colors = new List<Color>();
        }

        static Material glowMat, reflMat;

        public static Material Mat(float intensity = 1.8f)
        {
            if (glowMat == null)
            {
                var sh = Shader.Find("NeonDrift/GlowAdditive");
                glowMat = sh != null ? new Material(sh) : Sprites.Unlit;
                if (sh != null) glowMat.SetFloat("_Intensity", intensity);
            }
            return glowMat;
        }

        static Material ReflMat()
        {
            if (reflMat == null)
            {
                var sh = Shader.Find("NeonDrift/GlowAdditive");
                reflMat = sh != null ? new Material(sh) : Sprites.Unlit;
                if (sh != null) reflMat.SetFloat("_Intensity", 0.9f);
            }
            return reflMat;
        }

        /// <summary>
        /// 像素本身够亮：暖色（窗光、灯笼、火）门槛低一些；
        /// 青色、蓝色门槛高很多——原图里大片的冷色多半是被霓虹照亮的屋顶和墙面，不是灯。
        /// </summary>
        static bool Bright(Color32 c, out float lum)
        {
            lum = (c.r + c.g + c.b) / 3f;
            if (c.a == 0) return false;
            int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            bool cool = c.b > c.r + 30 || (c.g > c.r + 30 && c.b > c.r + 15);
            if (cool) return false;                                         // 冷色一律不当光源
            return (lum > 125f && mx - mn > 70) || lum > 190f;
        }

        public static Info Extract(Texture2D tex, Vector2 pivot01, int maxSpots = 3, bool strict = true)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, count = 0;
            var outPx = new Color32[px.Length];
            const int Cell = 8;
            int cw = (w + Cell - 1) / Cell, ch = (h + Cell - 1) / Cell;
            var cellN = new int[cw * ch];
            var cellC = new Vector3[cw * ch];
            // 每个像素和周围 9×9 的平均亮度比：比周围亮出一截才算光源（整片均匀发亮的表面不算）
            var lum = new float[px.Length];
            var cand = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) cand[i] = Bright(px[i], out lum[i]);
            var local = BoxBlur(lum, w, h, 4);
            if (strict) for (int i = 0; i < px.Length; i++) cand[i] = cand[i] && lum[i] > local[i] + 28f;
            // 连成大片的亮区是被照亮的表面，不是灯：去掉；太碎的也去掉
            int opaque = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a > 0) opaque++;
            int maxBlob = strict ? Mathf.Max(60, opaque / 50) : int.MaxValue;
            var seen = new bool[px.Length];
            var stack = new Stack<int>();
            var comp = new List<int>();
            for (int i = 0; i < px.Length; i++)
            {
                if (!cand[i] || seen[i]) continue;
                comp.Clear(); stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int j = stack.Pop(); comp.Add(j);
                    int x = j % w, y = j / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int k = ny * w + nx;
                            if (cand[k] && !seen[k]) { seen[k] = true; stack.Push(k); }
                        }
                }
                if (comp.Count < 3 || comp.Count > maxBlob) continue;
                foreach (int j in comp)
                {
                    outPx[j] = px[j];
                    count++;
                    int x = j % w, y = j / w, ci = (y / Cell) * cw + x / Cell;
                    cellN[ci]++;
                    cellC[ci] += new Vector3(px[j].r, px[j].g, px[j].b);
                }
            }
            if (count < 6) return null;
            var gt = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            gt.SetPixels32(outPx);
            gt.Apply();
            var info = new Info { sprite = Sprite.Create(gt, new Rect(0, 0, w, h), pivot01, Sprites.PPU, 0, SpriteMeshType.FullRect) };

            // 最亮的几块：按发光像素数挑格子，挑中的周围 3 格内不再挑
            var used = new bool[cellN.Length];
            for (int k = 0; k < maxSpots; k++)
            {
                int best = -1;
                for (int i = 0; i < cellN.Length; i++) if (!used[i] && (best < 0 || cellN[i] > cellN[best])) best = i;
                if (best < 0 || cellN[best] < 6) break;
                int bx = best % cw, by = best / cw;
                for (int y = by - 3; y <= by + 3; y++)
                    for (int x = bx - 3; x <= bx + 3; x++)
                        if (x >= 0 && y >= 0 && x < cw && y < ch) used[y * cw + x] = true;
                Vector3 c = cellC[best] / cellN[best];
                float mx = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
                info.colors.Add(new Color(c.x / mx, c.y / mx, c.z / mx));
                float ux = ((bx + 0.5f) * Cell - pivot01.x * w) / Sprites.PPU, uy = ((by + 0.5f) * Cell - pivot01.y * h) / Sprites.PPU;
                info.spots.Add(new Vector3(ux, uy, cellN[best]));
            }
            return info;
        }

        static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length]; var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                float acc = 0; int n = 0;
                for (int x = -r; x < w; x++)
                {
                    if (x + r < w) { acc += src[y * w + x + r]; n++; }
                    if (x - r - 1 >= 0) { acc -= src[y * w + x - r - 1]; n--; }
                    if (x >= 0) tmp[y * w + x] = acc / n;
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0; int n = 0;
                for (int y = -r; y < h; y++)
                {
                    if (y + r < h) { acc += tmp[(y + r) * w + x]; n++; }
                    if (y - r - 1 >= 0) { acc -= tmp[(y - r - 1) * w + x]; n--; }
                    if (y >= 0) dst[y * w + x] = acc / n;
                }
            }
            return dst;
        }

        /// <summary>
        /// 给一个已经摆好的图挂上发光层、地面倒影和照到地上的灯。
        /// order：发光层的排序值；lights：最多放几盏灯；外加的渲染器会放进 extra，方便一起淡出。
        /// </summary>
        public static void Attach(Transform host, Transform floorParent, Info info, bool flip, int order, int lights, List<SpriteRenderer> extra, System.Random rng, bool flicker = true, bool reflect = true)
        {
            if (info == null) return;
            var g = new GameObject("Glow").AddComponent<SpriteRenderer>();
            g.transform.SetParent(host, false);
            g.sprite = info.sprite;
            g.flipX = flip;
            g.sharedMaterial = Mat();
            g.sortingOrder = order;
            extra?.Add(g);
            if (flicker && rng != null && rng.NextDouble() < 0.3) g.gameObject.AddComponent<NeonFlicker>();

            // 湿地倒影：以支点（底边）为轴往下翻，压扁一点，很淡
            if (reflect) Reflect(floorParent, host.position, info, flip, extra);

            // 照到地上的灯：放在发光处正下方贴近地面的位置
            for (int i = 0; i < Mathf.Min(lights, info.spots.Count); i++)
            {
                var s = info.spots[i];
                float x = flip ? -s.x : s.x;
                Vector2 pos = (Vector2)host.position + new Vector2(x, Mathf.Min(s.y, 1.2f) * 0.35f);
                float radius = Mathf.Clamp(1.4f + Mathf.Sqrt(s.z) * 0.25f, 1.8f, 3.5f);
                var col = info.colors[i];
                if (col.b > col.r + 0.1f || col.g > col.r + 0.25f) continue;          // 冷色的灯不放
                Atmosphere.Lamp(host, pos, col, radius, 0.55f);
            }
        }

        public static void Reflect(Transform floorParent, Vector2 axis, Info info, bool flip, List<SpriteRenderer> extra, float yOffset = 0f)
        {
            var r = new GameObject("Reflection").AddComponent<SpriteRenderer>();
            r.transform.SetParent(floorParent, true);
            r.transform.position = axis;
            r.transform.localScale = new Vector3(1f, 0.55f, 1f);
            r.sprite = info.sprite;
            r.flipX = flip;
            r.flipY = true;
            r.sharedMaterial = ReflMat();
            r.color = new Color(1f, 1f, 1f, 0.18f);
            r.sortingOrder = -29972;                                   // 在地面、贴花之上，在所有物件和角色之下
            extra?.Add(r);
        }
    }

    /// <summary>坏掉的霓虹：大部分时间亮着，偶尔闪几下或者暗一会儿。</summary>
    public class NeonFlicker : MonoBehaviour
    {
        SpriteRenderer sr;
        float t, next;
        void Awake() { sr = GetComponent<SpriteRenderer>(); next = Random.Range(1f, 6f); }
        void Update()
        {
            t += Time.deltaTime;
            if (t < next) return;
            float k = Random.value;
            var c = sr.color;
            c.a = k < 0.6f ? (c.a > 0.5f ? 0.15f : 1f) : 1f;                // 闪一下或恢复
            sr.color = c;
            t = 0f;
            next = c.a < 0.5f ? Random.Range(0.04f, 0.25f) : Random.Range(0.3f, 5f);
        }
    }
}
