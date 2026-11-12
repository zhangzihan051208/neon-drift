using UnityEngine;
using UnityEngine.Rendering;

namespace NeonDrift
{
    /// <summary>
    /// 斜 45° 的格子坐标。一格在屏幕上是 64×32 像素的菱形（2×1 单位），角色约 64 像素高，所以一屏只看得到街区的一部分（《哈迪斯》式近镜头）。
    /// 格子的 x、y 减小 = 往画面上方走。游戏逻辑（移动、子弹、碰撞）仍然在屏幕平面里算。
    /// </summary>
    public static class Iso
    {
        /// <summary>格子放大倍数。墙高、掩体高等竖直尺寸也按它放大。</summary>
        public const float S = 2f;
        public const float HW = 0.5f * S, HH = 0.25f * S;

        public static Vector2 W(float x, float y) => new Vector2((x - y) * HW, -(x + y) * HH);

        public static Vector2 Tile(Vector2 w)
        {
            float a = w.x / HW, b = -w.y / HH;
            return new Vector2((a + b) * 0.5f, (b - a) * 0.5f);
        }

        public static int Order(float worldY) => 1000 - Mathf.RoundToInt(worldY * 20f);

        /// <summary>用任意凸多边形生成一块纯色网格（世界坐标）。用网格而不是精灵，因为运行时生成的精灵不允许改形状。</summary>
        public static MeshRenderer Poly(Transform parent, string name, Vector2[] pts, Color color, int order, bool glow = false)
        {
            Vector2 min = pts[0];
            foreach (var p in pts) min = Vector2.Min(min, p);
            var verts = new Vector3[pts.Length];
            var cols = new Color[pts.Length];
            var uvs = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                verts[i] = pts[i] - min;
                cols[i] = color;
                uvs[i] = new Vector2(0.5f, 0.5f);
            }
            var tris = new int[(pts.Length - 2) * 3];
            for (int i = 0; i < pts.Length - 2; i++)
            {
                tris[i * 3] = 0; tris[i * 3 + 1] = i + 2; tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = name, vertices = verts, colors = cols, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.position = min;
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glow ? Sprites.Unlit : Sprites.Lit;      // 发光的（窗、霓虹、闸门）不受灯光影响
            mr.sortingOrder = order;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static Material groundMat;
        static bool groundTried;

        /// <summary>沥青地面材质：StreamingAssets/Ground/asphalt.png，每 8 格重复一次。读不到就返回 null。</summary>
        static Material GroundMat()
        {
            if (groundTried) return groundMat;
            groundTried = true;
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Ground", "asphalt.png");
            if (!System.IO.File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(path))) return null;
            tex.wrapMode = TextureWrapMode.Repeat;
            groundMat = new Material(Sprites.Lit) { mainTexture = tex };
            return groundMat;
        }

        public const float GroundRepeatTiles = 8f;

        /// <summary>一块铺了沥青贴图的地面（格子坐标的矩形）。贴图按格子坐标铺，所以相邻地块的纹理自然接上。</summary>
        public static void Ground(Transform parent, string name, float x0, float y0, float x1, float y1, Color fallback, int order, Color[] corners = null)
        {
            var mat = GroundMat();
            if (mat == null) { Poly(parent, name, new[] { W(x0, y0), W(x1, y0), W(x1, y1), W(x0, y1) }, fallback, order); return; }
            var tiles = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            Vector2 min = W(x0, y0);
            foreach (var t in tiles) min = Vector2.Min(min, W(t.x, t.y));
            var verts = new Vector3[4]; var uvs = new Vector2[4]; var cols = new Color[4];
            for (int i = 0; i < 4; i++)
            {
                verts[i] = W(tiles[i].x, tiles[i].y) - min;
                uvs[i] = tiles[i] / GroundRepeatTiles;
                cols[i] = corners != null ? corners[i] : Color.white;     // 四个角的颜色，用来往黑色渐隐
            }
            var mesh = new Mesh { name = name, vertices = verts, uv = uvs, colors = cols, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.position = min;
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = order;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>一块四个角颜色不同的地面叠层（格子坐标），用来做墙根暗影这类渐变。</summary>
        public static void Gradient(Transform parent, string name, Vector2[] tiles, Color[] cols, int order)
        {
            Vector2 min = W(tiles[0].x, tiles[0].y);
            foreach (var t in tiles) min = Vector2.Min(min, W(t.x, t.y));
            var verts = new Vector3[4]; var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++) { verts[i] = W(tiles[i].x, tiles[i].y) - min; uvs[i] = new Vector2(0.5f, 0.5f); }
            var mesh = new Mesh { name = name, vertices = verts, uv = uvs, colors = cols, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.position = min;
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Sprites.Unlit;
            mr.sortingOrder = order;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>立在格子线上的一片竖直面（墙、闸门、护栏）。a、b 是底边两端的格子坐标。</summary>
        public static MeshRenderer Face(Transform parent, string name, Vector2 a, Vector2 b, float height, Color color, int order, float z0 = 0f, bool glow = false)
        {
            height *= S; z0 *= S;
            Vector2 p0 = W(a.x, a.y) + Vector2.up * z0, p1 = W(b.x, b.y) + Vector2.up * z0, up = Vector2.up * height;
            return Poly(parent, name, new[] { p0, p1, p1 + up, p0 + up }, color, order, glow);
        }

        /// <summary>一个斜 45° 的箱子（掩体）：带碰撞，按最靠前的角排序。</summary>
        public static GameObject Box(Transform parent, string name, float x0, float y0, float x1, float y1, float height, Color top)
        {
            var root = new GameObject(name);
            root.transform.position = W(x1, y1);
            root.transform.SetParent(parent, true);
            Vector2 up = Vector2.up * height * S;
            Color left = top * 0.72f; left.a = 1f;
            Color right = top * 0.5f; right.a = 1f;
            Poly(root.transform, "Left", new[] { W(x0, y1), W(x1, y1), W(x1, y1) + up, W(x0, y1) + up }, left, 0);
            Poly(root.transform, "Right", new[] { W(x1, y0), W(x1, y1), W(x1, y1) + up, W(x1, y0) + up }, right, 0);
            Poly(root.transform, "Top", new[] { W(x0, y0) + up, W(x1, y0) + up, W(x1, y1) + up, W(x0, y1) + up }, top, 1);
            var col = root.AddComponent<PolygonCollider2D>();
            Vector2 o = root.transform.position;
            col.points = new[] { W(x0, y0) - o, W(x1, y0) - o, W(x1, y1) - o, W(x0, y1) - o };
            root.AddComponent<SortingGroup>();
            root.AddComponent<YSort>().isStatic = true;
            return root;
        }

        public static EdgeCollider2D Edge(GameObject host, Vector2 a, Vector2 b)
        {
            var e = host.AddComponent<EdgeCollider2D>();
            e.points = new[] { W(a.x, a.y), W(b.x, b.y) };
            return e;
        }
    }
}
