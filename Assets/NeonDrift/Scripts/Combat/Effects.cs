using UnityEngine;

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
                var f = sr.gameObject.AddComponent<Fader>();
                f.velocity = Random.insideUnitCircle.normalized * Random.Range(speed * 0.4f, speed);
                f.life = f.maxLife = Random.Range(0.12f, 0.25f);
            }
        }

        public static void Burst(Vector2 pos, Color color)
        {
            Sparks(pos, color, 14, 7f);
            Sparks(pos, Color.white, 5, 4f);
        }

        /// <summary>闪移残影：复制角色身体的剪影并淡出。</summary>
        public static void Afterimage(SpriteRenderer src, Color color)
        {
            var sr = Sprites.Make(null, "Afterimage", src.sprite, color, src.sortingOrder - 1, src.transform.position);
            sr.transform.localScale = src.transform.lossyScale;
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

    public class Pulse : MonoBehaviour
    {
        void Update()
        {
            float s = 0.8f + Mathf.PingPong(Time.time * 4f, 0.4f);
            transform.localScale = Vector3.one * s;
        }
    }
}
