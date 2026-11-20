using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 子弹不用刚体，每帧用 CircleCast 检测：
    /// 碰到敌对单位就造成伤害（能穿透的继续飞）；碰到墙且还有反弹次数就反弹；爆炸弹命中或飞到头时爆炸。
    /// 外观来自 PixelFx（像素子弹 + 命中 / 爆炸动画），按武器颜色发光。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        static readonly List<Projectile> all = new List<Projectile>();

        public Team team;
        Vector2 dir;
        float speed, damage, life, maxLife, radius, explode, explodeDamage;
        int bounces, pierce;
        Color color;
        string kind;
        SpriteRenderer sr;
        Sprite[] frames;
        readonly HashSet<Health> hitOnce = new HashSet<Health>();

        /// <summary>敌人和旧代码用的简单版本：手枪样式的子弹。</summary>
        public static Projectile Spawn(Vector2 pos, Vector2 dir, Team team, float speed, float damage, int bounces, Color color,
                                       float radius = 0.1f, float life = 2.5f)
            => Spawn(pos, dir, team, speed, damage, bounces, color, "pistol", radius, life, 2f);

        public static Projectile Spawn(Vector2 pos, Vector2 dir, Team team, float speed, float damage, int bounces, Color color,
                                       string kind, float radius, float life, float trailPx, int pierce = 0, float explode = 0f, float explodeDamage = 0f)
        {
            var go = new GameObject(team == Team.Player ? "Bullet" : "EnemyBullet");
            go.transform.position = pos;
            var p = go.AddComponent<Projectile>();
            p.team = team; p.dir = dir.normalized; p.speed = speed; p.damage = damage;
            p.bounces = bounces; p.color = color; p.radius = radius; p.life = p.maxLife = life;
            p.kind = kind; p.pierce = pierce; p.explode = explode; p.explodeDamage = explodeDamage;
            p.frames = PixelFx.Bullet(kind);
            p.sr = go.AddComponent<SpriteRenderer>();
            p.sr.sprite = p.frames[0];
            p.sr.sharedMaterial = Glow.Mat();              // 子弹自己发光
            p.sr.color = color;
            p.sr.sortingOrder = 5000;
            go.transform.right = p.dir;
            if (trailPx > 0f)
            {
                var tr = go.AddComponent<TrailRenderer>(); // 很短很细的光尾
                tr.time = kind == "sniper" ? 0.09f : 0.035f;
                tr.startWidth = trailPx / Sprites.PPU; tr.endWidth = 0f;
                tr.sharedMaterial = Glow.MeshMat();
                var tc = color; tc.a = 0.5f;
                tr.startColor = tc; tr.endColor = new Color(color.r, color.g, color.b, 0f);
                tr.sortingOrder = 4999;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            all.Add(p);
            return p;
        }

        public static void ClearAll()
        {
            foreach (var p in all) if (p) Destroy(p.gameObject);
            all.Clear();
        }

        /// <summary>刀劈子弹：扇形范围内不属于 owner 的子弹全部劈碎。返回劈掉了几颗。</summary>
        public static int CutInArc(Vector2 center, Vector2 facing, float range, float arcDeg, Team owner)
        {
            int n = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var p = all[i];
                if (p == null || p.team == owner) continue;
                Vector2 d = (Vector2)p.transform.position - center;
                if (d.magnitude > range || Vector2.Angle(facing, d) > arcDeg * 0.5f) continue;
                PixelFx.Play(PixelFx.Impact(4), p.transform.position, 0f, Color.white, 24f);
                Destroy(p.gameObject); n++;
            }
            return n;
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            life -= dt;
            float u = 1f - life / Mathf.Max(0.0001f, maxLife);
            if (frames.Length > 1)                                                    // 火焰、电弧：按飞行进度换帧
                sr.sprite = frames[Mathf.Min(frames.Length - 1, Mathf.FloorToInt(u * frames.Length * (kind == "bolt" ? 4f : 1f)) % frames.Length)];
            if (kind == "flame") { transform.localScale = Vector3.one * (1f + u * 1.4f); var c = color; c.a = 1f - u * 0.7f; sr.color = c; speed *= 1f - dt * 2.5f; }
            if (life <= 0f) { if (explode > 0f) Explode(transform.position); Destroy(gameObject); return; }

            Vector2 pos = transform.position;
            float dist = speed * dt;
            var hits = Physics2D.CircleCastAll(pos, radius, dir, dist);
            foreach (var hit in hits)
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null)
                {
                    if (h.team == team || h.IsDead || h.IsInvulnerable || hitOnce.Contains(h)) continue;  // 闪移中可以穿过子弹
                    if (explode > 0f) { Explode(hit.point); Destroy(gameObject); return; }
                    if (h.Damage(damage, dir))
                    {
                        if (kind != "flame")
                        {
                            PixelFx.Play(PixelFx.Impact(kind == "sniper" ? 7 : kind == "pellet" ? 3 : 5), hit.point, 0f, color, 26f);
                            Effects.Flash(hit.point, color, 2.2f, 1.2f, 0.07f);
                        }
                        if (team == Team.Player) CameraRig.Shake(kind == "sniper" ? 0.12f : 0.05f);
                    }
                    if (pierce > 0) { pierce--; hitOnce.Add(h); continue; }               // 穿透：继续飞
                    Destroy(gameObject);
                    return;
                }
                if (hit.collider.isTrigger) continue;

                // 墙
                if (explode > 0f) { Explode(hit.point + hit.normal * 0.1f); Destroy(gameObject); return; }
                if (bounces > 0 && hit.distance > 0f)
                {
                    bounces--;
                    transform.position = hit.centroid + hit.normal * 0.02f;
                    dir = Vector2.Reflect(dir, hit.normal).normalized;
                    transform.right = dir;
                    PixelFx.Play(PixelFx.Impact(3), hit.point, 0f, color, 30f);
                    return;
                }
                if (kind != "flame") PixelFx.Play(PixelFx.Impact(4), hit.point, 0f, color, 26f);
                Destroy(gameObject);
                return;
            }
            transform.position = pos + dir * dist;
        }

        /// <summary>爆炸：像素爆炸动画 + 一闪的大光 + 范围伤害 + 震屏。</summary>
        void Explode(Vector2 at)
        {
            int rpx = Mathf.RoundToInt(explode * Sprites.PPU * 0.75f);
            PixelFx.Play(PixelFx.Explosion(rpx), at, 0f, color, 18f, 6001);
            Effects.Flash(at, color, explode * 3f, 2.4f, 0.18f);
            Effects.Sparks(at, color, 10, 6f);
            CameraRig.Shake(0.25f);
            GameRoot.Hitstop(0.04f);
            foreach (var c in Physics2D.OverlapCircleAll(at, explode))
            {
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.team == team || h.IsDead) continue;
                if (!hitOnce.Add(h)) continue;
                Vector2 d = (Vector2)h.transform.position - at;
                h.Damage(explodeDamage, d.sqrMagnitude > 0.001f ? d.normalized : dir);
            }
        }
    }
}
