using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 风：大部分时间只有一点微风，隔十来秒来一阵。Gust（0–1）给所有会被吹动的东西用：
    /// 房子和物件上标出来的局部（WindSway 着色器）、伸出墙外的竖招牌、雨丝的斜度、火苗。
    /// </summary>
    public class Wind : MonoBehaviour
    {
        public static float Gust { get; private set; }
        static Wind inst;
        static Material mat;
        static bool matTried;
        static readonly Dictionary<Texture2D, Sprite[]> cache = new Dictionary<Texture2D, Sprite[]>();      // 按贴图本身缓存（Unity 6 里 GetInstanceID 已经不能用）
        static readonly int GustId = Shader.PropertyToID("_NDGust"), TimeId = Shader.PropertyToID("_NDWindT");
        static readonly int RectId = Shader.PropertyToID("_Rect"), WindId = Shader.PropertyToID("_Wind");
        float seed, phase;

        public static void Ensure()
        {
            if (inst != null) return;
            inst = new GameObject("[Wind]").AddComponent<Wind>();
        }

        void Awake() { seed = Random.value * 100f; }

        void Update()
        {
            float t = Time.time;
            float n = Mathf.PerlinNoise(seed, t * 0.09f);                       // 很慢的起伏
            float gust = Mathf.SmoothStep(0.5f, 0.75f, n);                      // 一多半时间是 0，偶尔升到 1
            float flutter = 0.75f + 0.25f * Mathf.PerlinNoise(seed + 7f, t * 1.7f);   // 一阵风里也有强有弱
            Gust = Mathf.Clamp01(0.08f + gust * flutter);
            phase += Time.deltaTime * (2.2f + Gust * 5f);
            Shader.SetGlobalFloat(GustId, Gust);
            Shader.SetGlobalFloat(TimeId, phase);
            Atmosphere.SetRainWind(Gust);
        }

        static Material Mat
        {
            get
            {
                if (mat == null && !matTried)
                {
                    matTried = true;
                    var sh = Shader.Find("NeonDrift/WindSway");
                    if (sh != null) mat = new Material(sh);
                    else Debug.LogWarning("找不到 NeonDrift/WindSway 着色器，风吹效果不显示");
                }
                return mat;
            }
        }

        /// <summary>
        /// 在 host（已经摆好的那张图，支点 pivot01）上面盖几块会被风吹动的局部。rects 用图片像素坐标（左上角为原点）。
        /// </summary>
        public static void Attach(Transform host, Texture2D tex, Vector2 pivot01, Props.WindRect[] rects, bool flip, int order, Color color, List<SpriteRenderer> extra)
        {
            if (rects == null || rects.Length == 0 || tex == null || Mat == null) return;
            Ensure();
            int W = tex.width, H = tex.height;
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                int x = Mathf.Clamp(r.x, 0, W - 2), y = Mathf.Clamp(r.y, 0, H - 2);
                int w = Mathf.Clamp(r.w, 2, W - x), h = Mathf.Clamp(r.h, 2, H - y);
                int yb = H - y - h;                                             // 贴图坐标是左下角为原点
                if (!cache.TryGetValue(tex, out var arr) || arr == null || arr.Length != rects.Length) cache[tex] = arr = new Sprite[rects.Length];
                var sp = arr[i];
                if (sp == null)
                    arr[i] = sp = Sprite.Create(tex, new Rect(x, yb, w, h), new Vector2(0.5f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
                var sr = new GameObject("Wind").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(host, false);
                float lx = (x + w * 0.5f - pivot01.x * W) / Sprites.PPU, ly = (yb + h * 0.5f - pivot01.y * H) / Sprites.PPU;
                sr.transform.localPosition = new Vector3(flip ? -lx : lx, ly, 0f);
                sr.sprite = sp;
                sr.flipX = flip;
                sr.sharedMaterial = Mat;
                sr.sortingOrder = order;
                sr.color = color;
                var mpb = new MaterialPropertyBlock();
                sr.GetPropertyBlock(mpb);
                mpb.SetVector(RectId, new Vector4(x / (float)W, yb / (float)H, w / (float)W, h / (float)H));
                float mode = r.mode == "ripple" ? 2f : r.mode == "top" ? 1f : 0f;
                mpb.SetVector(WindId, new Vector4(mode, r.amp, (host.position.x * 1.7f + i * 2.3f) % 6.28f, flip ? 1f : -1f));
                sr.SetPropertyBlock(mpb);
                extra?.Add(sr);
            }
        }
    }

    /// <summary>伸出墙外的竖招牌：来风的时候轻轻晃。</summary>
    public class SignSway : MonoBehaviour
    {
        float seed;
        void Awake() { seed = Random.value * 10f; Wind.Ensure(); }
        void Update()
        {
            float a = Mathf.Sin(Time.time * 3.1f + seed) * 2.2f * Mathf.Max(0f, Wind.Gust - 0.15f);
            transform.localRotation = Quaternion.Euler(0f, 0f, a);
        }
    }

    /// <summary>
    /// 火桶的火会慢慢烧完：角色第一次走近后开始计时，一两分钟里火苗越来越小、光越来越暗，最后只剩一点余烬。
    /// </summary>
    public class FireLife : MonoBehaviour
    {
        public ParticleSystem flames, sparks;
        public Flicker lamp;
        public SpriteRenderer glow;
        public float duration = 90f;
        float baseRate, baseSparks, baseLamp, baseSize0, baseSize1, t;
        bool started;

        void Start()
        {
            if (flames != null) { baseRate = flames.emission.rateOverTime.constant; baseSize0 = flames.main.startSize.constantMin; baseSize1 = flames.main.startSize.constantMax; }
            if (sparks != null) baseSparks = sparks.emission.rateOverTime.constant;
            if (lamp != null) baseLamp = lamp.baseIntensity;
            duration *= Random.Range(0.8f, 1.3f);
        }

        void Update()
        {
            if (!started)
            {
                var pl = GameRoot.I != null ? GameRoot.I.Player : null;
                if (pl == null || ((Vector2)pl.transform.position - (Vector2)transform.position).sqrMagnitude > 18f * 18f) return;
                started = true;
            }
            if (t >= duration) return;
            t = Mathf.Min(duration, t + Time.deltaTime);
            float u = t / duration;
            float k = 1f - u * u * (3f - 2f * u);                               // 1 → 0，开头和结尾变化慢
            float gustKick = 1f + Wind.Gust * 0.25f * k;                         // 风一吹火旺一下
            if (flames != null)
            {
                var em = flames.emission; em.rateOverTime = baseRate * Mathf.Lerp(0.07f, 1f, k) * gustKick;
                var m = flames.main; float s = Mathf.Lerp(0.4f, 1f, k);
                m.startSize = new ParticleSystem.MinMaxCurve(baseSize0 * s, baseSize1 * s);
            }
            if (sparks != null) { var em = sparks.emission; em.rateOverTime = baseSparks * Mathf.Lerp(0.15f, 1f, k); }
            if (lamp != null) lamp.baseIntensity = baseLamp * Mathf.Lerp(0.12f, 1f, k);
            if (glow != null) { var c = glow.color; c.a = Mathf.Lerp(0.25f, 1f, k); glow.color = c; }
        }
    }
}
