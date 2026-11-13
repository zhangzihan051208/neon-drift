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
            p.sr.sprite = Sprites.Circle(8);
            p.sr.sharedMaterial = Glow.Mat();              // 子弹自己发光
            p.sr.color = color;
            p.sr.sortingOrder = 5000;
            float sc = radius * 2f / (8f / Sprites.PPU) * 1.4f;
            go.transform.localScale = new Vector3(sc * 2.2f, sc * 0.8f, 1f);      // 拉成一道短短的曳光
            go.transform.right = p.dir;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.07f;
            tr.startWidth = radius * 1.6f; tr.endWidth = 0f;
            tr.sharedMaterial = Glow.MeshMat();
            var tc = color; tc.a = 0.7f;
            tr.startColor = tc; tr.endColor = new Color(color.r, color.g, color.b, 0f);
            tr.sortingOrder = 4999;
            tr.numCapVertices = 2;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            all.Add(p);
            return p;
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
