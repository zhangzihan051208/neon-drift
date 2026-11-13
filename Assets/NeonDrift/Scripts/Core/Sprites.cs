using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>灰盒阶段用代码生成的纯色精灵。正式美术接入后替换。</summary>
    public static class Sprites
    {
        public const int PPU = 32;

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Material unlit, lit;

        /// <summary>受 2D 灯光影响的材质：地面、墙、掩体用它。</summary>
        public static Material Lit
        {
            get
            {
                if (lit == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                    lit = sh != null ? new Material(sh) : Unlit;
                }
                return lit;
            }
        }

        public static Material Unlit
        {
            get
            {
                if (unlit == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    unlit = new Material(sh);
                }
                return unlit;
            }
        }

        static Material meshUnlit, meshLit;

        /// <summary>
        /// 代码画的网格（护栏、平台侧面、水管）专用的材质，自带一张纯白贴图。
        /// 不能和精灵共用同一个材质：网格自己没有贴图，共用时会采到上一张精灵图的颜色，于是同一条护栏一段亮一段暗、走动时还会变。
        /// </summary>
        public static Material MeshUnlit
        {
            get
            {
                if (meshUnlit == null) meshUnlit = new Material(Unlit) { name = "MeshUnlit", mainTexture = Texture2D.whiteTexture };
                return meshUnlit;
            }
        }

        public static Material MeshLit
        {
            get
            {
                if (meshLit == null) meshLit = new Material(Lit) { name = "MeshLit", mainTexture = Texture2D.whiteTexture };
                return meshLit;
            }
        }

        /// <summary>白色方块，pivot 默认居中；feet=true 时 pivot 在底边中点。</summary>
        public static Sprite Box(int w, int h, bool feet = false)
        {
            string key = $"box{w}x{h}{feet}";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), feet ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect);
            cache[key] = s;
            return s;
        }

        public static Sprite Circle(int d)
        {
            string key = $"circle{d}";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(d, d);
            var px = new Color32[d * d];
            float r = d / 2f;
            for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    px[y * d + x] = dx * dx + dy * dy <= r * r ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), PPU);
            cache[key] = s;
            return s;
        }

        /// <summary>32×32 的地板格子，带一圈暗边，平铺后能看出格子尺寸。</summary>
        public static Sprite FloorTile()
        {
            const string key = "floor";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(32, 32);
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    px[y * 32 + x] = (x == 0 || y == 0) ? new Color32(205, 205, 215, 255) : new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0f, 0f), PPU, 0, SpriteMeshType.FullRect);
            cache[key] = s;
            return s;
        }

        static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        public static SpriteRenderer Make(Transform parent, string name, Sprite sprite, Color color, int order, Vector2 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            sr.sharedMaterial = Unlit;
            return sr;
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }
    }
}
