using System.Collections.Generic;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 敌人基类：生成外观、受击击退、死亡、通知房间。
    /// 脚下一圈淡淡的红光表示敌对；被骇入倒戈（门客的协议、策反的炮台）后变成金色，替玩家打其他敌人。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class Enemy : MonoBehaviour, IHackable
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        public static readonly Color HostileRing = new Color(1f, 0.23f, 0.23f, 0.55f);
        public static readonly Color AllyRing = new Color(1f, 0.81f, 0.23f, 0.7f);

        protected Rigidbody2D rb;
        protected Health hp;
        protected SpriteRenderer body;
        protected float stun;
        protected Color baseColor;
        SpriteRenderer ring;
        float paralyzeT, sparkT, allyT, phase;

        /// <summary>倒戈中：替玩家打其他敌人，不算在房间剩余敌人里。</summary>
        public bool Allied { get; private set; }
        public Health Hp => hp;
        protected Team MyTeam => Allied ? Team.Player : Team.Enemy;

        protected PlayerController Player => GameRoot.I != null ? GameRoot.I.Player : null;
        protected Vector2 Pos => transform.position;
        protected Vector2 Center => (Vector2)transform.position + Vector2.up * (BodyPixels.y * Size / Sprites.PPU * 0.5f);

        /// <summary>灰盒敌人整体放大的倍数（主角放大以后，原来的敌人只到她腰）。</summary>
        public const float Size = 1.8f;

        public static T Spawn<T>(Vector2 pos) where T : Enemy
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.position = pos;
            go.AddComponent<Rigidbody2D>();
            var e = go.AddComponent<T>();
            e.Build();
            return e;
        }

        protected abstract float MaxHp { get; }
        /// <summary>打倒后掉几枚信用芯片（每枚 1 信用点）。</summary>
        protected virtual int CreditDrop => 3;
        /// <summary>打倒后掉一格残电（+1 电量）的概率。</summary>
        protected virtual float EnergyChance => 0.6f;
        /// <summary>打倒后掉一发子弹的概率（子弹是稀缺资源）。</summary>
        protected virtual float AmmoChance => 0.12f;
        public bool Paralyzed => paralyzeT > 0f;
        protected abstract Color BodyColor { get; }
        protected virtual Vector2Int BodyPixels => new Vector2Int(24, 44);
        /// <summary>挨打时被击退的速度、硬直（壮汉几乎不退）。</summary>
        protected virtual float KnockbackTaken => 6.5f;
        protected virtual float HitStunTaken => 0.12f;
        /// <summary>近战敌人（拾荒刀手、穿刺手、锅炉工）；远程的枪手和炮台会躲在近战后面。</summary>
        public virtual bool IsMelee => true;

        void Build()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 0f;

            var feet = gameObject.AddComponent<CircleCollider2D>();
            feet.radius = 0.44f;
            feet.offset = new Vector2(0f, 0.25f);
            var hurt = gameObject.AddComponent<BoxCollider2D>();
            hurt.isTrigger = true;
            int bw = Mathf.RoundToInt(BodyPixels.x * Size), bh = Mathf.RoundToInt(BodyPixels.y * Size);
            float h = bh / (float)Sprites.PPU;
            hurt.size = new Vector2(bw / (float)Sprites.PPU, h);
            hurt.offset = new Vector2(0f, h / 2f);

            baseColor = BodyColor;
            body = Sprites.Make(transform, "Body", Sprites.Box(bw, bh, feet: true), baseColor, 0, Vector2.zero);
            var eyes = Sprites.Make(transform, "Eyes", Sprites.Box(22, 5), Sprites.Hex("ff3b3b"), 1, new Vector2(0f, h - 0.5f));
            eyes.sharedMaterial = Glow.Mat();                     // 红眼睛在暗处发光，远远就能看见敌人
            gameObject.AddComponent<YSort>();
            GroundShadow.Attach(transform, bw / (float)Sprites.PPU * 1.6f);

            // 脚下的敌我光圈：敌人红、倒戈的金
            float rw = Mathf.Max(1.3f, bw / (float)Sprites.PPU * 1.5f);
            ring = Sprites.Make(transform, "Ring", PixelFx.GroundRing(), HostileRing, -2, new Vector2(0f, 0.08f));
            ring.sharedMaterial = Glow.Mat();
            float nat = PixelFx.GroundRing().bounds.size.x;
            ring.transform.localScale = new Vector3(rw / nat, rw / nat, 1f);
            phase = Random.value * 6.28f;

            hp = gameObject.AddComponent<Health>();
            hp.Init(Team.Enemy, MaxHp);
            hp.Damaged += (amt, dir) => { stun = Mathf.Max(stun, HitStunTaken); rb.linearVelocity = dir.normalized * KnockbackTaken; };
            hp.Died += Die;
            OnBuilt();
        }

        protected virtual void OnBuilt() { }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Die()
        {
            Effects.Burst(Center, baseColor);
            CameraRig.Shake(0.12f);
            GameRoot.Hitstop(0.035f);
            Protocols.OnEnemyDied(this);
            if (!Allied) Loot.KillDrop(transform.parent, Center, CreditDrop, EnergyChance, AmmoChance);
            Player?.Hacker?.OnKill();
            GameRoot.I?.Room?.OnEnemyKilled(this);
            Destroy(gameObject);
        }

        /// <summary>房间清完时，还在帮忙的倒戈单位断电下线（不掉东西）。</summary>
        public void Dismiss()
        {
            PixelFx.Play(PixelFx.Glitch(10), Center, 0f, Hacker.HackCol, 18f);
            Effects.Sparks(Center, AllyRing, 6, 3f);
            Destroy(gameObject);
        }

        void Update()
        {
            // 光圈慢慢呼吸
            float a = (Allied ? AllyRing.a : HostileRing.a) * (0.75f + 0.25f * Mathf.Sin(Time.time * 3f + phase));
            var c = Allied ? AllyRing : HostileRing; c.a = a; ring.color = c;
            // 麻痹：冒电火花
            if (paralyzeT > 0f)
            {
                paralyzeT -= Time.deltaTime;
                sparkT -= Time.deltaTime;
                if (sparkT <= 0f) { sparkT = 0.12f; Effects.Sparks(Center + Random.insideUnitCircle * 0.5f, Sprites.Hex("fff0b0"), 1, 2f); }
            }
            // 临时倒戈到时间就变回来
            if (Allied && allyT > 0f)
            {
                allyT -= Time.deltaTime;
                if (allyT <= 0f) Revert();
            }
        }

        protected virtual void FixedUpdate()
        {
            if (stun > 0f)
            {
                stun -= Time.fixedDeltaTime;
                rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 40f * Time.fixedDeltaTime);
                return;
            }
            if (Allied)
            {
                // 倒戈：打离自己最近的敌人；没有敌人就原地待命
                var target = NearestHostile(Pos, 14f, this);
                if (target == null) { rb.linearVelocity = Vector2.zero; return; }
                Think(target.transform.position, target.hp);
                return;
            }
            var p = Player;
            if (p == null || p.IsDead) { rb.linearVelocity = Vector2.zero; return; }
            Think(p.transform.position, p.Hp);
        }

        /// <summary>每个物理帧的行为：target 是要打的对象（玩家，或倒戈后的敌人）。子弹用 MyTeam。</summary>
        protected abstract void Think(Vector2 target, Health targetHp);
        protected virtual void OnAllegianceChanged() { }

        /// <summary>出招前的预警色（教玩家读招）。</summary>
        protected void Telegraph(bool on)
        {
            hp.SetBase(body, on ? Sprites.Hex("ffd23f") : baseColor);
        }

        // ---------------------------------------------------------------- 骇入
        public void Paralyze(float t)
        {
            stun = Mathf.Max(stun, t);
            paralyzeT = Mathf.Max(paralyzeT, t);
            rb.linearVelocity = Vector2.zero;
        }

        /// <summary>倒戈：seconds ≤ 0 表示一直倒戈到房间结束。</summary>
        public void Convert(float seconds)
        {
            Allied = true;
            allyT = seconds;
            hp.team = Team.Player;
            Telegraph(false);
            PixelFx.Play(PixelFx.Glitch(10), Center, 0f, Hacker.HackCol, 18f);
            OnAllegianceChanged();
        }

        void Revert()
        {
            Allied = false;
            hp.team = Team.Enemy;
            PixelFx.Play(PixelFx.Glitch(10), Center, 0f, HostileRing, 18f);
            OnAllegianceChanged();
        }

        public virtual Vector2 HackPoint => Center;
        public virtual int HackCost => 1;
        public virtual bool CanHack => !Allied && !hp.IsDead;
        public virtual string HackName => "Short Circuit";

        /// <summary>默认骇入「短路」：麻痹 1.5 秒 + 2 点电击。门客「倒戈」改为让它倒戈 4 秒。</summary>
        public virtual void OnHacked(PlayerController p)
        {
            if (p.N("m_turn") > 0) Convert(4f);
            else
            {
                Paralyze(1.5f);
                hp.Damage(2f, Vector2.zero, DamageKind.Shock);
            }
            Protocols.OnHackEnemy(p, this);
        }

        public static Enemy Nearest(Vector2 from, float maxDist) => NearestHostile(from, maxDist, null);

        /// <summary>最近的敌对单位（不算倒戈的）。</summary>
        public static Enemy NearestHostile(Vector2 from, float maxDist, Enemy except)
        {
            Enemy best = null;
            float bd = maxDist * maxDist;
            foreach (var e in All)
            {
                if (e == null || e == except || e.Allied) continue;
                float d = ((Vector2)e.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
    }
}
