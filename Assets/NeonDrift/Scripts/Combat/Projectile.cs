using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 子弹不用刚体，每个物理帧用 CircleCast 检测：
    /// 碰到敌对单位就造成伤害；碰到墙且还有反弹次数就反弹（动能枪的核心规则）。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        static readonly List<Projectile> all = new List<Projectile>();

        Team team;
        Vector2 dir;
        float speed, damage, life, radius;
        int bounces;
        Color color;
        SpriteRenderer sr;

        public static Projectile Spawn(Vector2 pos, Vector2 dir, Team team, float speed, float damage, int bounces, Color color,
                                       float radius = 0.1f, float life = 2.5f)
        {
            var go = new GameObject(team == Team.Player ? "Bullet" : "EnemyBullet");
            go.transform.position = pos;
            var p = go.AddComponent<Projectile>();
            p.team = team; p.dir = dir.normalized; p.speed = speed; p.damage = damage;
            p.bounces = bounces; p.color = color; p.radius = radius; p.life = life;
            p.sr = go.AddComponent<SpriteRenderer>();
            p.sr.sprite = BulletSprite();                  // 6×3 像素的小弹头：一道亮芯 + 一圈颜色，按飞行方向转
            p.sr.sharedMaterial = Glow.Mat();              // 子弹自己发光
            p.sr.color = color;
            p.sr.sortingOrder = 5000;
            go.transform.right = p.dir;
            var tr = go.AddComponent<TrailRenderer>();     // 很短很细的光尾
            tr.time = 0.035f;
            tr.startWidth = 2f / Sprites.PPU; tr.endWidth = 0f;
            tr.sharedMaterial = Glow.MeshMat();
            var tc = color; tc.a = 0.5f;
            tr.startColor = tc; tr.endColor = new Color(color.r, color.g, color.b, 0f);
            tr.sortingOrder = 4999;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            all.Add(p);
            return p;
        }

        static Sprite bullet;

        /// <summary>
        /// 简约的小子弹：6×3 像素，中间一行是白色亮芯（越靠前越亮），上下一行是浅灰外圈，被 sr.color 染成子弹的颜色。
        /// 像素是硬边的，按 32 像素/单位，和场景一样大小。
        /// </summary>
        static Sprite BulletSprite()
        {
            if (bullet != null) return bullet;
            const int w = 6, h = 3;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "bullet" };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                byte core = (byte)(150 + x * 21);                          // 尾部暗、弹头亮
                px[1 * w + x] = new Color32(255, 255, 255, core);
                if (x >= 1 && x <= 4)
                {
                    byte rim = (byte)(60 + x * 25);
                    px[0 * w + x] = new Color32(200, 200, 200, rim);
                    px[2 * w + x] = new Color32(200, 200, 200, rim);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            bullet = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.7f, 0.5f), Sprites.PPU, 0, SpriteMeshType.FullRect);
            return bullet;
        }

        public static void ClearAll()
        {
            foreach (var p in all) if (p) Destroy(p.gameObject);
            all.Clear();
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            life -= dt;
            if (life <= 0f) { Destroy(gameObject); return; }

            Vector2 pos = transform.position;
            float dist = speed * dt;
            var hits = Physics2D.CircleCastAll(pos, radius, dir, dist);
            foreach (var hit in hits)
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null)
                {
                    if (h.team == team || h.IsDead || h.IsInvulnerable) continue; // 闪移中可以穿过子弹
                    if (h.Damage(damage, dir))
                    {
                        Effects.Sparks(hit.point, color, 6, 4f);
                        Effects.Flash(hit.point, color, 2.2f, 1.2f, 0.07f);
                        if (team == Team.Player) CameraRig.Shake(0.05f);
                    }
                    Destroy(gameObject);
                    return;
                }
                if (hit.collider.isTrigger) continue;

                // 墙
                if (bounces > 0 && hit.distance > 0f)
                {
                    bounces--;
                    transform.position = hit.centroid + hit.normal * 0.02f;
                    dir = Vector2.Reflect(dir, hit.normal).normalized;
                    transform.right = dir;
                    Effects.Sparks(hit.point, color, 3, 2.5f);
                    return;
                }
                Effects.Sparks(hit.point, color, 4, 3f);
                Destroy(gameObject);
                return;
            }
            transform.position = pos + dir * dist;
        }
    }
}
