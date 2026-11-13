using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonDrift
{
    /// <summary>灰盒用的简易特效：火花、残影、刷怪预警。</summary>
    public static class Effects
    {
        public static void Sparks(Vector2 pos, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var sr = Sprites.Make(null, "Spark", Sprites.Box(3, 3), color, 6000, pos);
                sr.sharedMaterial = Glow.Mat();                       // 火花自己发光，会被泛光晕开
                var f = sr.gameObject.AddComponent<Fader>();
                f.velocity = Random.insideUnitCircle.normalized * Random.Range(speed * 0.4f, speed);
                f.life = f.maxLife = Random.Range(0.12f, 0.25f);
            }
        }

        public static void Burst(Vector2 pos, Color color)
        {
            Sparks(pos, color, 14, 7f);
            Sparks(pos, Color.white, 5, 4f);
            Flash(pos, Color.Lerp(color, Color.white, 0.5f), 3.5f, 1.6f, 0.12f);
        }

        /// <summary>一闪而过的光：枪口火光、命中、爆开。夜里的场景全靠它把动作“打亮”。</summary>
        public static void Flash(Vector2 pos, Color color, float radius, float intensity, float life)
        {
            var l = Atmosphere.Lamp(null, pos, color, radius, intensity);
            var f = l.gameObject.AddComponent<LightFade>();
            f.life = f.maxLife = life; f.start = intensity;
        }

        /// <summary>脚下溅起的水：湿地上走路、闪移、落地时用。不发光，颜色很淡。</summary>
        public static void Splash(Vector2 pos, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var sr = Sprites.Make(null, "Splash", Sprites.Box(2, 2), new Color(0.62f, 0.72f, 0.78f, 0.55f), 0, pos + Random.insideUnitCircle * 0.12f);
                sr.sortingOrder = -29960;                               // 贴着地面，在角色和物件下面
                var f = sr.gameObject.AddComponent<Fader>();
                var v = Random.insideUnitCircle; v.y = Mathf.Abs(v.y) * 0.6f + 0.2f;
                f.velocity = v * Random.Range(speed * 0.5f, speed);
                f.life = f.maxLife = Random.Range(0.14f, 0.26f);
            }
        }

        /// <summary>闪移残影：复制角色身体的剪影并淡出。</summary>
        public static void Afterimage(SpriteRenderer src, Color color)
        {
            var sr = Sprites.Make(null, "Afterimage", src.sprite, color, src.sortingOrder - 1, src.transform.position);
            sr.transform.localScale = src.transform.lossyScale;
            sr.flipX = src.flipX;
            sr.flipY = src.flipY;
            var f = sr.gameObject.AddComponent<Fader>();
            f.life = f.maxLife = 0.18f;
        }

        public static GameObject SpawnMarker(Vector2 pos, Color color)
        {
            var sr = Sprites.Make(null, "SpawnMarker", Sprites.Circle(24), color, 1, pos);
            sr.gameObject.AddComponent<Pulse>();
            return sr.gameObject;
        }
    }

    public class Fader : MonoBehaviour
    {
        public Vector2 velocity;
        public float life = 0.2f, maxLife = 0.2f;
        SpriteRenderer sr;
        Color c0;

        void Start() { sr = GetComponent<SpriteRenderer>(); c0 = sr.color; }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }
            transform.position += (Vector3)(velocity * Time.deltaTime);
            velocity *= 1f - 6f * Time.deltaTime;
            var c = c0; c.a *= life / maxLife; sr.color = c;
        }
    }

    public class LightFade : MonoBehaviour
    {
        public float life = 0.08f, maxLife = 0.08f, start = 1f;
        Light2D l;
        void Start() { l = GetComponent<Light2D>(); }
        void Update()
        {
            life -= Time.unscaledDeltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }
            if (l != null) l.intensity = start * (life / maxLife);
        }
    }

    /// <summary>
    /// 角色脚下的接地阴影：一团柔和的扁椭圆，贴在地面上跟着走。没有它，角色像贴在画面上的纸片。
    /// 不挂在角色下面（角色有排序组，会把阴影带到物件前面去）。
    /// </summary>
    public class GroundShadow : MonoBehaviour
    {
        static Sprite sprite;
        Transform target;
        SpriteRenderer sr;

        public static void Attach(Transform target, float width, float alpha = 0.55f)
        {
            if (sprite == null)
            {
                const int W = 64, H = 32;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                        px[y * W + x] = new Color32(0, 0, 0, (byte)(255 * a * a * (3f - 2f * a)));
                    }
                tex.SetPixels32(px); tex.Apply();
                sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
            }
            var go = new GameObject("GroundShadow");
            var gs = go.AddComponent<GroundShadow>();
            gs.target = target;
            gs.sr = go.AddComponent<SpriteRenderer>();
            gs.sr.sprite = sprite;
            gs.sr.sharedMaterial = Sprites.Unlit;
            gs.sr.color = new Color(0f, 0f, 0f, alpha);
            gs.sr.sortingOrder = -29974;
            go.transform.localScale = new Vector3(width / 2f, width / 2f, 1f);
            go.transform.position = target.position;
        }

        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            transform.position = target.position + Vector3.up * 0.08f;
            var body = target.GetComponentInChildren<SpriteRenderer>();
            sr.enabled = target.gameObject.activeInHierarchy && body != null && body.enabled;
        }
    }

    public class Pulse : MonoBehaviour
    {
        void Update()
        {
            float s = 0.8f + Mathf.PingPong(Time.time * 4f, 0.4f);
            transform.localScale = Vector3.one * s;
        }
    }
}
