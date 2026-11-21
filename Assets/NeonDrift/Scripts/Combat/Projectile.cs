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
        // 稀有武器的特殊效果（由 PlayerController 在生成后设置）
        public float homing;      // 追踪：每秒最多转多少度
        public int chain;         // 命中后电弧跳几个敌人
        public float burn;        // 点燃：每秒伤害
        public WeaponDef weapon;  // 玩家的子弹：哪把枪打的（命中时按这把枪的暴击率判定暴击）
        Vector2 dir;
        float speed, damage, life, maxLife, radius, explode, explodeDamage;
        int bounces, pierce;
        Color color;
        string kind;
        SpriteRenderer sr;
        Sprite[] frames;
        readonly HashSet<Health> hitOnce = new HashSet<Health>();
        float age, emitT;
        static bool Loops(string k) => k == "smart" || k == "stinger" || k == "missile" || k == "orb" || k == "ember" || k == "shuriken" || k == "chakram" || k == "caltrop";
        /// <summary>投掷冷兵器：飞完之后变成地上可以捡回来的东西。</summary>
        public WeaponDef retrieve;
        bool returning;
        public void SetLife(float t) { life = maxLife = t; }

        /// <summary>投掷物停下来的地方：留一个可以捡的；铁蒺藜撒成一片。</summary>
        void Finish(Vector2 at, bool stuck)
        {
            if (retrieve == null) return;
            if (kind == "caltrop") CaltropField.Spawn(at, retrieve, damage);
            else ThrownItem.Spawn(at, retrieve, frames[0], dir, stuck);
            retrieve = null;
        }

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

        public static void ClearAll(bool keepThrown = false)
        {
            foreach (var p in all) if (p) { if (keepThrown) p.Finish(p.transform.position, false); Destroy(p.gameObject); }
            all.Clear();
        }

        /// <summary>刀劈子弹：扇形范围内不属于 owner 的子弹全部劈碎。返回劈掉了几颗。</summary>
        public static int CutInArc(Vector2 center, Vector2 facing, float range, float arcDeg, Team owner, bool reflect = false)
        {
            int n = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var p = all[i];
                if (p == null || p.team == owner) continue;
                Vector2 d = (Vector2)p.transform.position - center;
                if (d.magnitude > range || Vector2.Angle(facing, d) > arcDeg * 0.5f) continue;
                PixelFx.Play(PixelFx.Impact(4), p.transform.position, 0f, Color.white, 24f);
                if (reflect)                                                                     // 无铭：把劈掉的子弹原路弹回去
                {
                    var back = Spawn(p.transform.position, -p.dir, owner, p.speed * 1.4f, 1.5f, 0, Weapons.Signature.color, "pistol", 0.12f, 1.5f, 2f);
                    back.weapon = Weapons.Signature;
                }
                Destroy(p.gameObject); n++;
            }
            if (n > 0) Sfx.Play("bullet_cut", 1f, center, 0.08f, 0.05f);
            return n;
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            life -= dt;
            float u = 1f - life / Mathf.Max(0.0001f, maxLife);
            age += dt;
            if (frames.Length > 1 && Loops(kind)) sr.sprite = frames[Mathf.FloorToInt(age * 18f) % frames.Length];   // 导弹尾焰、电球：按时间循环
            else if (frames.Length > 1)                                               // 火焰、电弧：按飞行进度换帧
                sr.sprite = frames[Mathf.Min(frames.Length - 1, Mathf.FloorToInt(u * frames.Length * (kind == "bolt" ? 4f : 1f)) % frames.Length)];
            if (kind == "flame") { transform.localScale = Vector3.one * (1f + u * 1.4f); var c = color; c.a = 1f - u * 0.7f; sr.color = c; speed *= 1f - dt * 2.5f; }
            if (life <= 0f) { if (explode > 0f) Explode(transform.position); Finish(transform.position, false); Destroy(gameObject); return; }

            Vector2 pos = transform.position;
            if (kind == "chakram" && team == Team.Player)                             // 月轮：飞出去 0.42 秒后掉头飞回手里
            {
                var owner = GameRoot.I != null ? GameRoot.I.Player : null;
                if (!returning && age > 0.42f) { returning = true; hitOnce.Clear(); }
                if (returning && owner != null)
                {
                    Vector2 home = (Vector2)owner.transform.position + Vector2.up * 1.1f, to = home - pos;
                    if (to.magnitude < 0.6f) { owner.ReturnThrown(retrieve ?? weapon, 1); retrieve = null; Destroy(gameObject); return; }
                    dir = to.normalized; speed = Mathf.Max(speed, 16f); life = Mathf.Max(life, 0.5f);
                }
            }
            if (homing > 0f && team == Team.Player) Steer(pos, dt);
            EmitTrail(pos, dt);
            float dist = speed * dt;
            var hits = Physics2D.CircleCastAll(pos, radius, dir, dist);
            foreach (var hit in hits)
            {
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null)
                {
                    if (team == Team.Enemy && h.IsInvulnerable)                                   // 子弹擦过闪移中的零：完美闪避
                    {
                        var pl = GameRoot.I != null ? GameRoot.I.Player : null;
                        if (pl != null && pl.Hp == h && pl.IsDashing) pl.PerfectDodge();
                    }
                    if (h.team == team || h.IsDead || h.IsInvulnerable || hitOnce.Contains(h)) continue;  // 闪移中可以穿过子弹
                    if (explode > 0f) { Explode(hit.point); Destroy(gameObject); return; }
                    // 暴击在命中时判定：这样“对麻痹 / 着火的敌人暴击率更高”之类的效果才能生效
                    var shooter = team == Team.Player && GameRoot.I != null ? GameRoot.I.Player : null;
                    Enemy he = shooter != null ? h.GetComponent<Enemy>() : null;
                    bool isCrit = shooter != null && he != null && shooter.RollCrit(weapon, he);
                    float dmg = isCrit ? damage * shooter.CritMulFor(he) : damage;
                    if (h.Damage(dmg, dir, kind == "flame" || kind == "ember" ? DamageKind.Fire : kind == "bolt" || kind == "orb" ? DamageKind.Shock : DamageKind.Bullet))
                    {
                        if (isCrit) shooter.OnCrit(hit.point, he);
                        else if (team == Team.Player) Sfx.Play("enemy_hit", 0.7f, hit.point, 0.1f, 0.05f);
                        if (kind == "javelin" && he != null) he.Paralyze(1f);                       // 标枪：钉住
                        if (burn > 0f) Burning.Apply(h, burn, color);
                        if (chain > 0) Chain(hit.point, h, chain, damage * 0.6f, color, team);
                        if (team == Team.Player && (chain > 0 || kind == "bolt" || kind == "orb")) ShockPuddle.Conduct(h.transform.position);
                        if (kind != "flame")
                        {
                            HitFx(hit.point);
                            Effects.Flash(hit.point, color, kind == "rail" ? 3.5f : 2.2f, 1.2f, 0.07f);
                        }
                        if (team == Team.Player) CameraRig.Shake(kind == "sniper" || kind == "rail" || kind == "slug" ? 0.12f : 0.05f);
                    }
                    if (pierce > 0) { pierce--; hitOnce.Add(h); continue; }               // 穿透：继续飞
                    Finish(hit.point - dir * 0.2f, false);
                    Destroy(gameObject);
                    return;
                }
                if (hit.collider.isTrigger) continue;

                // 墙
                if (explode > 0f) { Explode(hit.point + hit.normal * 0.1f); Destroy(gameObject); return; }
                if (kind == "chakram") { returning = true; hitOnce.Clear(); transform.position = hit.centroid + hit.normal * 0.05f; return; }
                if (bounces > 0 && hit.distance > 0f)
                {
                    bounces--;
                    transform.position = hit.centroid + hit.normal * 0.02f;
                    dir = Vector2.Reflect(dir, hit.normal).normalized;
                    transform.right = dir;
                    PixelFx.Play(PixelFx.Impact(3), hit.point, 0f, color, 30f);
                    return;
                }
                if (kind == "nail") StickNail(hit.point);
                else if (kind == "orb") PixelFx.Play(PixelFx.Zap(7), hit.point, 0f, color, 24f);
                else if (kind != "flame") PixelFx.Play(PixelFx.Impact(4), hit.point, 0f, color, 26f);
                Finish(hit.point - dir * 0.12f, true);                                     // 扎在墙上
                Destroy(gameObject);
                return;
            }
            transform.position = pos + dir * dist;
        }

        /// <summary>飞行中留下的东西：导弹的尾烟、追踪弹的光点、电球的火花、燃烧弹丸的火星。</summary>
        void EmitTrail(Vector2 pos, float dt)
        {
            emitT -= dt;
            if (emitT > 0f) return;
            Vector2 tail = pos - dir * 0.25f;
            switch (kind)
            {
                case "missile":
                    emitT = 0.035f;
                    PixelFx.Play(PixelFx.Puff(3), tail + Random.insideUnitCircle * 0.05f, 0f, new Color(0.55f, 0.5f, 0.48f, 0.7f), 14f, 4990);
                    break;
                case "smart": case "stinger":
                    emitT = 0.03f;
                    PixelFx.Play(PixelFx.Dot(), tail, 0f, color, 16f, 4990);
                    break;
                case "orb":
                    emitT = 0.06f;
                    PixelFx.Play(PixelFx.Dot(), pos + Random.insideUnitCircle * 0.25f, 0f, color, 14f, 4990);
                    break;
                case "ember":
                    emitT = 0.05f;
                    if (Random.value < 0.6f) PixelFx.Play(PixelFx.Dot(), tail + Random.insideUnitCircle * 0.06f, 0f, color, 12f, 4990);
                    break;
                default: emitT = 1f; break;
            }
        }

        /// <summary>按子弹种类挑命中特效。</summary>
        void HitFx(Vector2 at)
        {
            switch (kind)
            {
                case "sniper": PixelFx.Play(PixelFx.Impact(7), at, 0f, color, 26f); break;
                case "rail": PixelFx.Play(PixelFx.Impact(9), at, 0f, color, 24f); PixelFx.Play(PixelFx.Zap(8), at, 0f, color, 22f); break;
                case "slug": PixelFx.Play(PixelFx.Impact(7), at, 0f, color, 26f); Effects.Sparks(at, color, 5, 4f); break;
                case "orb": PixelFx.Play(PixelFx.Zap(9), at, 0f, color, 22f); break;
                case "bolt": PixelFx.Play(PixelFx.Zap(5), at, 0f, color, 26f); break;
                case "smart": case "stinger": PixelFx.Play(PixelFx.Cross(3), at, 45f, color, 26f); break;
                case "pellet": case "ember": PixelFx.Play(PixelFx.Impact(3), at, 0f, color, 28f); break;
                case "nail": PixelFx.Play(PixelFx.Impact(3), at, 0f, color, 28f); break;
                default: PixelFx.Play(PixelFx.Impact(5), at, 0f, color, 26f); break;
            }
        }

        /// <summary>钉子打在墙上：钉在那里 1.5 秒，然后淡掉。</summary>
        void StickNail(Vector2 at)
        {
            var go = new GameObject("Nail");
            go.transform.position = at - dir * 0.08f;
            go.transform.right = dir;
            var nsr = go.AddComponent<SpriteRenderer>();
            nsr.sprite = frames[0];
            nsr.color = new Color(0.75f, 0.68f, 0.6f);
            nsr.sortingOrder = 4990;
            PixelFx.Play(PixelFx.Impact(3), at, 0f, color, 28f);
            Effects.Sparks(at, color, 3, 2f);
            Destroy(go, 1.5f);
        }

        /// <summary>追踪：朝前方 110° 内最近的敌人慢慢拐过去。</summary>
        void Steer(Vector2 pos, float dt)
        {
            Enemy best = null; float bd = 9f;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.Allied) continue;
                Vector2 d = (Vector2)e.transform.position + Vector2.up * 0.9f - pos;
                float m = d.magnitude;
                if (m < bd && Vector2.Angle(dir, d) < 110f) { bd = m; best = e; }
            }
            if (best == null) return;
            Vector2 to = ((Vector2)best.transform.position + Vector2.up * 0.9f - pos).normalized;
            float a = Vector2.SignedAngle(dir, to);
            a = Mathf.Clamp(a, -homing * dt, homing * dt);
            dir = ((Vector2)(Quaternion.Euler(0, 0, a) * dir)).normalized;
            transform.right = dir;
        }

        /// <summary>电弧：从命中点依次跳到最近的、还没被这道电弧打过的敌人（3.5 格内）。</summary>
        public static void Chain(Vector2 from, Health first, int jumps, float dmg, Color color, Team team)
        {
            var done = new HashSet<Health> { first };
            for (int j = 0; j < jumps; j++)
            {
                Health next = null; Vector2 at = from; float bd = 3.5f;
                foreach (var e in Enemy.All)
                {
                    if (e == null) continue;
                    var h = e.GetComponent<Health>();
                    if (h == null || h.IsDead || h.team == team || done.Contains(h)) continue;
                    Vector2 c = (Vector2)e.transform.position + Vector2.up * 0.9f;
                    float m = Vector2.Distance(c, from);
                    if (m < bd) { bd = m; next = h; at = c; }
                }
                if (next == null) return;
                done.Add(next);
                ArcFx(from, at, color);
                next.Damage(dmg, (at - from).normalized, DamageKind.Shock);
                if (team == Team.Player) ShockPuddle.Conduct(next.transform.position);
                PixelFx.Play(PixelFx.Impact(4), at, 0f, color, 26f);
                from = at;
            }
        }

        /// <summary>一道一闪而过的折线电弧。</summary>
        public static void ArcFx(Vector2 a, Vector2 b, Color color)
        {
            var go = new GameObject("Arc");
            var lr = go.AddComponent<LineRenderer>();
            int n = 7;
            lr.positionCount = n;
            Vector2 nrm = Vector2.Perpendicular(b - a).normalized;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float off = (i == 0 || i == n - 1) ? 0f : Random.Range(-0.18f, 0.18f);
                lr.SetPosition(i, Vector2.Lerp(a, b, t) + nrm * off);
            }
            lr.startWidth = lr.endWidth = 2f / Sprites.PPU;
            lr.sharedMaterial = Glow.MeshMat();
            lr.startColor = lr.endColor = color;
            lr.sortingOrder = 5001;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Effects.Flash(b, color, 1.6f, 1f, 0.06f);
            Destroy(go, 0.08f);
        }

        /// <summary>爆炸：像素爆炸动画 + 一闪的大光 + 范围伤害 + 震屏。</summary>
        void Explode(Vector2 at)
        {
            int rpx = Mathf.RoundToInt(explode * Sprites.PPU * 0.75f);
            PixelFx.Play(PixelFx.Explosion(rpx), at, 0f, color, 18f, 6001);
            Sfx.Play("explosion", 1f, at, 0.08f, 0.08f);
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
                h.Damage(explodeDamage, d.sqrMagnitude > 0.001f ? d.normalized : dir, DamageKind.Explosion);
            }
        }
    }

    /// <summary>燃烧：每 0.3 秒掉一次血，持续 2.5 秒；再被点燃只刷新时间、取较高的伤害。</summary>
    public class Burning : MonoBehaviour
    {
        Health h; float dps, left, tick; Color color;
        GameObject flame;

        public static void Apply(Health target, float dps, Color color)
        {
            if (target == null || target.IsDead) return;
            var b = target.GetComponent<Burning>();
            if (b == null) { b = target.gameObject.AddComponent<Burning>(); b.h = target; b.tick = 0.3f; }
            b.dps = Mathf.Max(b.dps, dps);
            b.left = 2.5f;
            b.color = color;
            if (b.flame == null)
            {
                var fsr = PixelFx.Play(PixelFx.BurnFlame(), (Vector2)target.transform.position + Vector2.up * 0.9f, 0f, color, 12f, 6000, false, target.transform);
                var fa = fsr.GetComponent<FxAnim>(); fa.loop = true;
                b.flame = fsr.gameObject;
            }
        }

        void OnDestroy() { if (flame != null) Destroy(flame); }

        void Update()
        {
            if (h == null || h.IsDead) { Destroy(this); return; }
            left -= Time.deltaTime; tick -= Time.deltaTime;
            if (tick <= 0f)
            {
                tick = 0.3f;
                h.Damage(dps * 0.3f, Vector2.zero, DamageKind.Fire);
                Effects.Sparks((Vector2)transform.position + Vector2.up * Random.Range(0.4f, 1.6f), color, 2, 1.2f);
            }
            if (left <= 0f) Destroy(this);
        }
    }
}
